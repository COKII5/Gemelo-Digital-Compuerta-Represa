/*
 * ============================================================================
 *  Digital Twin - Dam Sluice Gate (Situation 4)
 *  Laboratorio: Gemelo Digital con Servomotor - Universidad San Buenaventura
 * ============================================================================
 *
 *  Hardware
 *    - Arduino UNO / Nano (ATmega328P)
 *    - Position servo (Futaba S3003) signal on D9
 *    - Potentiometer (10k) wiper on A0, ends on 5V / GND
 *    - Green LED on D5: ON while the gate has any opening
 *    - Red LED on D7: ON only when the gate is fully closed (0 deg)
 *      Each LED: pin -> 220 ohm resistor -> LED anode (long leg), cathode -> GND
 *      Power-on self-test: green blinks 0.6 s, then red 0.6 s.
 *    - If the servo resets the board, power it from an external 5V supply
 *      and join the grounds.
 *
 *  Note on Servo.h: on the ATmega328P the library uses Timer1, so
 *  analogWrite() (PWM) is no longer available on pins 9 and 10.
 *
 *  Serial protocol (9600 baud, text, '\n' terminated)
 *    Unity  -> Arduino : A:<angle>          angle is an integer 0..180
 *    Arduino -> Unity  : S:<angle>,<source> source = POT | DIG
 *
 *  Useful angular range (defined by the group)
 *    0 deg  = gate fully closed
 *    90 deg = gate fully open (100 % opening)
 *    The pot is mapped to this range. A: commands inside the protocol range
 *    (0..180) are accepted and clamped to the useful range; anything outside
 *    0..180 or malformed is discarded.
 *
 *  The main loop never calls delay(): every task is scheduled with millis().
 * ============================================================================
 */

#include <Servo.h>

// ----------------------------------------------------------------------------
// Hardware configuration
// ----------------------------------------------------------------------------
const uint8_t SERVO_PIN = 9;
const uint8_t POT_PIN = A0;
const uint8_t GREEN_LED_PIN = 5;  // ON while the gate has any opening
const uint8_t RED_LED_PIN = 7;    // ON only when the gate is fully closed
const unsigned long LED_TEST_STEP_MS = 600;  // power-on self-test, per LED

// ----------------------------------------------------------------------------
// Angular ranges
// ----------------------------------------------------------------------------
const int PROTOCOL_MIN_DEG = 0;     // valid range for A: commands
const int PROTOCOL_MAX_DEG = 180;
const int USEFUL_MIN_DEG = 0;       // gate closed
const int USEFUL_MAX_DEG = 90;      // gate fully open

// ----------------------------------------------------------------------------
// Timing (all non-blocking)
// ----------------------------------------------------------------------------
const unsigned long POT_PERIOD_MS = 10;     // pot sampling, 100 Hz
const unsigned long MOTION_PERIOD_MS = 20;  // servo update = one PWM frame (50 Hz)
const unsigned long REPORT_PERIOD_MS = 50;  // S: report, 20 Hz (see README)
const float MAX_SPEED_DEG_PER_S = 60.0f;    // configurable maximum slew rate

// ----------------------------------------------------------------------------
// Arbitration policy between the physical (pot) and digital (Unity) controls
// ----------------------------------------------------------------------------
#define POLICY_POT_ALWAYS 0  // baseline: pot applied every cycle (digital is lost)
#define POLICY_LAST_MOVER 1  // last control that moves wins, deadband on the pot
#define POLICY_PICKUP 2      // pot regains control only when it "catches" the target

#define ARBITRATION_POLICY POLICY_LAST_MOVER

const float POT_FILTER_ALPHA = 0.20f;  // EMA filter for the analog reading
const float POT_JITTER_DEG = 1.0f;     // hysteresis while the pot is in control
const float POT_TAKEOVER_DEG = 4.0f;   // LAST_MOVER: pot movement needed to take control back
const float POT_NOISE_DEG = 1.5f;      // PICKUP: minimum movement to be considered intentional
const float PICKUP_WINDOW_DEG = 2.0f;  // PICKUP: |pot - target| needed to pick up
// Pot calibration. With the knob at its closed end stop this setup reads about
// 6 deg (raw ~68 of 1023) instead of 0, because of contact resistance in the
// breadboard. Raw values at or below POT_RAW_CLOSED count as 0 deg and the rest
// of the travel is rescaled, so the full 0..90 range is still available.
const int POT_RAW_CLOSED = 80;   // measured ~68 at the end stop, plus margin
const int POT_RAW_OPEN = 1023;   // raw value at the open end stop
// Readings this close to an end stop are snapped to it (noise margin).
const float POT_END_SNAP_DEG = 3.0f;

// ----------------------------------------------------------------------------
// Serial reception
// ----------------------------------------------------------------------------
const uint8_t RX_BUFFER_SIZE = 16;  // "A:180" needs 6 bytes; longer lines are discarded

enum ControlSource : uint8_t { SOURCE_POT, SOURCE_DIG };

// ----------------------------------------------------------------------------
// State
// ----------------------------------------------------------------------------
Servo gateServo;

float potAngle = 0.0f;         // filtered pot position mapped to degrees
float potLatchedAngle = 0.0f;  // pot position when the digital control took over
float targetAngle = 0.0f;      // target set by the control that currently rules
float currentAngle = 0.0f;     // commanded intermediate angle (ramp)
ControlSource source = SOURCE_POT;

unsigned long lastPotMs = 0;
unsigned long lastMotionMs = 0;
unsigned long lastReportMs = 0;
unsigned long bootMs = 0;

char rxBuffer[RX_BUFFER_SIZE];
uint8_t rxLength = 0;
bool rxOverflow = false;

// ----------------------------------------------------------------------------
// Helpers
// ----------------------------------------------------------------------------
float clampFloat(float value, float low, float high) {
  if (value < low) return low;
  if (value > high) return high;
  return value;
}

float readPotDegrees() {
  int raw = analogRead(POT_PIN);  // 0..1023
  float fraction = (float)(raw - POT_RAW_CLOSED) / (float)(POT_RAW_OPEN - POT_RAW_CLOSED);
  fraction = clampFloat(fraction, 0.0f, 1.0f);
  return USEFUL_MIN_DEG + (USEFUL_MAX_DEG - USEFUL_MIN_DEG) * fraction;
}

int roundToInt(float value) {
  return (int)(value + 0.5f);  // values are always >= 0 here
}

// ----------------------------------------------------------------------------
// Digital command parsing: "A:<0..180>"
// ----------------------------------------------------------------------------
bool parseAngleCommand(const char* message, int* outAngle) {
  if (message[0] != 'A' || message[1] != ':') return false;

  const char* cursor = message + 2;
  if (*cursor == '\0') return false;  // "A:" without digits

  long value = 0;
  uint8_t digits = 0;
  while (*cursor != '\0') {
    if (*cursor < '0' || *cursor > '9') return false;  // signs, spaces, letters...
    value = value * 10 + (*cursor - '0');
    if (++digits > 3) return false;  // more than 3 digits is always out of range
    cursor++;
  }

  if (value < PROTOCOL_MIN_DEG || value > PROTOCOL_MAX_DEG) return false;
  *outAngle = (int)value;
  return true;
}

void applyDigitalCommand(int angle) {
  targetAngle = clampFloat((float)angle, USEFUL_MIN_DEG, USEFUL_MAX_DEG);
  source = SOURCE_DIG;
  potLatchedAngle = potAngle;  // remember where the knob was left
}

void handleMessage(const char* message) {
  int angle;
  if (parseAngleCommand(message, &angle)) {
    applyDigitalCommand(angle);
  }
  // Any other message is silently ignored.
}

void readSerialNonBlocking() {
  while (Serial.available() > 0) {
    char c = (char)Serial.read();
    if (c == '\r') continue;

    if (c == '\n') {
      if (!rxOverflow && rxLength > 0) {
        rxBuffer[rxLength] = '\0';
        handleMessage(rxBuffer);
      }
      rxLength = 0;
      rxOverflow = false;
    } else if (rxLength < RX_BUFFER_SIZE - 1) {
      rxBuffer[rxLength++] = c;
    } else {
      rxOverflow = true;  // discard the whole line
    }
  }
}

// ----------------------------------------------------------------------------
// Physical control and arbitration
// ----------------------------------------------------------------------------
float snapPotToEnds(float angle) {
  if (angle <= USEFUL_MIN_DEG + POT_END_SNAP_DEG) return USEFUL_MIN_DEG;
  if (angle >= USEFUL_MAX_DEG - POT_END_SNAP_DEG) return USEFUL_MAX_DEG;
  return angle;
}

void followPot(bool force) {
  float desired = snapPotToEnds(potAngle);
  // Hysteresis: small analog fluctuations do not move the target.
  if (force || fabs(desired - targetAngle) >= POT_JITTER_DEG) {
    targetAngle = (float)roundToInt(desired);
  }
}

void arbitrate() {
#if ARBITRATION_POLICY == POLICY_POT_ALWAYS
  // Naive approach: the pot overrides any digital command in the next cycle.
  bool wasDigital = (source == SOURCE_DIG);
  source = SOURCE_POT;
  followPot(wasDigital);

#elif ARBITRATION_POLICY == POLICY_LAST_MOVER
  // The digital command rules until the knob is intentionally moved
  // POT_TAKEOVER_DEG away from where it was when the command arrived.
  if (source == SOURCE_DIG) {
    if (fabs(potAngle - potLatchedAngle) < POT_TAKEOVER_DEG) return;
    source = SOURCE_POT;
    followPot(true);
  } else {
    followPot(false);
  }

#elif ARBITRATION_POLICY == POLICY_PICKUP
  // Soft takeover: the knob must reach (or cross) the current target before
  // it controls the servo again, so the gate never jumps.
  if (source == SOURCE_DIG) {
    bool moved = fabs(potAngle - potLatchedAngle) >= POT_NOISE_DEG;
    bool near = fabs(potAngle - targetAngle) <= PICKUP_WINDOW_DEG;
    bool crossed = ((potLatchedAngle - targetAngle) * (potAngle - targetAngle)) < 0.0f;
    if (!(moved && (near || crossed))) return;
    source = SOURCE_POT;
    followPot(true);
  } else {
    followPot(false);
  }
#else
#error "Unknown ARBITRATION_POLICY"
#endif
}

void samplePot() {
  potAngle += POT_FILTER_ALPHA * (readPotDegrees() - potAngle);
}

// ----------------------------------------------------------------------------
// Status LEDs: driven by the commanded servo angle (same value reported in S:)
// ----------------------------------------------------------------------------
void updateStatusLeds() {
  // Power-on self-test (non-blocking): green first, then red. If a LED does
  // not blink here, the problem is its wiring, not the angle logic.
  unsigned long sinceBoot = millis() - bootMs;
  if (sinceBoot < 2 * LED_TEST_STEP_MS) {
    bool greenPhase = sinceBoot < LED_TEST_STEP_MS;
    digitalWrite(GREEN_LED_PIN, greenPhase ? HIGH : LOW);
    digitalWrite(RED_LED_PIN, greenPhase ? LOW : HIGH);
    return;
  }

  bool fullyClosed = roundToInt(currentAngle) <= USEFUL_MIN_DEG;  // value sent in S:
  digitalWrite(RED_LED_PIN, fullyClosed ? HIGH : LOW);
  digitalWrite(GREEN_LED_PIN, fullyClosed ? LOW : HIGH);
}

// ----------------------------------------------------------------------------
// Progressive motion (slew-rate limited)
// ----------------------------------------------------------------------------
void updateMotion(float deltaSeconds) {
  float maxStep = MAX_SPEED_DEG_PER_S * deltaSeconds;
  float error = targetAngle - currentAngle;

  if (fabs(error) <= maxStep) {
    currentAngle = targetAngle;
  } else {
    currentAngle += (error > 0.0f) ? maxStep : -maxStep;
  }
  gateServo.write(roundToInt(currentAngle));
  updateStatusLeds();
}

// ----------------------------------------------------------------------------
// State report: "S:<angle>,<POT|DIG>\n"
// ----------------------------------------------------------------------------
void sendReport() {
  Serial.print(F("S:"));
  Serial.print(roundToInt(currentAngle));
  Serial.print(',');
  Serial.print(source == SOURCE_POT ? F("POT") : F("DIG"));
  Serial.print('\n');
}

// ----------------------------------------------------------------------------
// Arduino entry points
// ----------------------------------------------------------------------------
void setup() {
  Serial.begin(9600);

  potAngle = readPotDegrees();
  targetAngle = (float)roundToInt(snapPotToEnds(potAngle));
  currentAngle = targetAngle;
  source = SOURCE_POT;

  pinMode(GREEN_LED_PIN, OUTPUT);
  pinMode(RED_LED_PIN, OUTPUT);

  gateServo.attach(SERVO_PIN);
  gateServo.write(roundToInt(currentAngle));

  unsigned long now = millis();
  bootMs = now;
  updateStatusLeds();
  lastPotMs = now;
  lastMotionMs = now;
  lastReportMs = now;
}

void loop() {
  unsigned long now = millis();

  readSerialNonBlocking();

  if (now - lastPotMs >= POT_PERIOD_MS) {
    lastPotMs = now;
    samplePot();
    arbitrate();
  }

  if (now - lastMotionMs >= MOTION_PERIOD_MS) {
    float deltaSeconds = (now - lastMotionMs) / 1000.0f;
    lastMotionMs = now;
    updateMotion(deltaSeconds);
  }

  if (now - lastReportMs >= REPORT_PERIOD_MS) {
    lastReportMs = now;
    sendReport();
  }
}
