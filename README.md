# Gemelo digital · Compuerta de represa (Situación 4)

Laboratorio *Gemelo Digital con Servomotor* — Universidad de San Buenaventura, Bogotá.

El ángulo de un servomotor controla la **apertura de la compuerta de fondo** de una represa. La escena de Unity simula un embalse con **caudal de entrada constante** y **caudal de salida que depende de la apertura**: el nivel del agua sube o baja en consecuencia y el flujo de salida es visible (chorro, canal aguas abajo y vertedero de excedencias). La sincronización es bidireccional: potenciómetro → servo → Unity, y deslizador de Unity → servo.

```
Gemelo-Digital-Compuerta-Represa/
├── Firmware/DamGateTwin/DamGateTwin.ino     Arduino (no bloqueante, protocolo A:/S:, arbitraje)
├── UnityProject/DamGateTwin/                Proyecto Unity 6 (Built-in Render Pipeline)
│   ├── Assets/Scripts/Communication/        Protocolo, puerto serial (hilo de fondo), emulador del firmware
│   ├── Assets/Scripts/Core/                 TwinController: estado del gemelo y envío de comandos
│   ├── Assets/Scripts/Simulation/           ReservoirModel: balance de masa del embalse
│   ├── Assets/Scripts/Visuals/              Compuerta, agua, partículas, cámara orbital
│   ├── Assets/Scripts/UI/                   HUD (se construye en tiempo de ejecución) y gráfica de nivel
│   ├── Assets/Scripts/Experiments/          Registro CSV y medición del tiempo de estabilización
│   └── Assets/Editor/                       Generador de la escena y ajustes del proyecto
├── Informe/
│   ├── datos/                               CSV de las pruebas (experimentos A y B)
│   ├── figuras/                             Capturas de la escena (compuerta cerrada y abierta)
│   └── esquema_circuito.tex                 Esquema del circuito (circuitikz)
└── README.md
```

## 1. Montaje

| Elemento | Conexión |
|---|---|
| Servo Futaba S3003 – señal (naranja) | D9 |
| Servo – V+ (rojo) / GND (café) | 5 V / GND (fuente externa con tierra común si el Arduino se reinicia) |
| Potenciómetro 10 kΩ – cursor | A0 |
| Potenciómetro – extremos | 5 V y GND |
| LED verde (ánodo, pata larga) | D5 con resistencia de 220 Ω en serie; cátodo a GND |
| LED rojo (ánodo, pata larga) | D7 con resistencia de 220 Ω en serie; cátodo a GND |

Indicadores: el **LED verde** se enciende mientras la compuerta tenga cualquier apertura (ángulo > 0°) y el **LED rojo** solo cuando está completamente cerrada (0°). Los dos dependen del mismo ángulo que se reporta en `S:`, así que la escena de Unity los replica sobre el polipasto y en el panel de estado. No use D0/D1 (puerto serie) ni D9 (servo). Al encender, el firmware hace una autoprueba: el verde se enciende 0,6 s y luego el rojo 0,6 s; si alguno no parpadea, el fallo está en su cableado. Las lecturas del potenciómetro a menos de 3° de cada tope se ajustan al tope (`POT_END_SNAP_DEG`), porque un potenciómetro real rara vez llega exactamente a 0 V o 5 V.

Coloque un indicador (flecha de cartón o horn) en el eje del servo para medir con transportador.

## 2. Firmware

1. Abra `Firmware/DamGateTwin/DamGateTwin.ino` en el Arduino IDE y cárguelo (placa UNO/Nano).
2. **Cierre el Monitor Serie** antes de conectar Unity (el puerto solo admite un programa a la vez).

Parámetros principales (inicio del archivo):

| Constante | Valor | Justificación |
|---|---|---|
| `USEFUL_MIN_DEG` / `USEFUL_MAX_DEG` | 0° / 90° | 0° = compuerta cerrada, 90° = apertura total (2 m). El potenciómetro recorre este rango. |
| `MAX_SPEED_DEG_PER_S` | 60 °/s | Velocidad máxima de giro configurable (rampa); el reporte muestra los ángulos intermedios. |
| `MOTION_PERIOD_MS` | 20 ms | Igual al período de la señal PWM del servo (50 Hz): actualizar más rápido no tiene efecto. |
| `REPORT_PERIOD_MS` | 50 ms (20 Hz) | Un mensaje `S:90,DIG\n` ocupa ≈ 9 bytes → 180 B/s, ≈ 19 % de los 960 B/s de 9600 baudios. A 60 °/s el ángulo cambia 3° entre reportes, suficiente para una animación fluida (Unity interpola). |
| `POT_PERIOD_MS` | 10 ms | Muestreo del potenciómetro con filtro EMA (α = 0,2). |

`loop()` no usa `delay()`: cada tarea se agenda con `millis()`. Los mensajes `A:` se leen carácter a carácter; los mal formados (`A:`, `A:12x`, `A:-5`), fuera de 0–180 o más largos que el búfer se descartan sin afectar el funcionamiento.

### Política de arbitraje (`ARBITRATION_POLICY`)

| Política | Regla | Falla cuando… |
|---|---|---|
| `POLICY_POT_ALWAYS` (base) | El potenciómetro se aplica en cada ciclo. | Siempre: el comando digital se pierde en ≤ 10 ms. Sirve como referencia en el experimento B. |
| **`POLICY_LAST_MOVER`** (elegida) | Prevalece el último mando que se movió. Al recibir `A:` se guarda la posición del potenciómetro; este recupera el control solo si se mueve ≥ 4° desde esa posición (umbral mayor que el ruido analógico, ≈ ±1°). Con el potenciómetro al mando hay histéresis de 1°. | Al recuperar el mando físico la compuerta salta a la posición de la perilla (a 60 °/s); un toque accidental ≥ 4° anula el comando digital. |
| `POLICY_PICKUP` | “Recogida”: el potenciómetro retoma el control solo cuando alcanza o cruza el ángulo actual. | No hay saltos, pero el operador debe buscar la posición; si no sabe dónde está, parece que la perilla “no responde”. |

Para cambiarla, edite `#define ARBITRATION_POLICY` y vuelva a cargar el firmware. El emulador de Unity implementa las tres políticas (botón *Política de arbitraje*).

## 3. Proyecto Unity

1. Unity Hub → **Add project from disk** → `UnityProject/DamGateTwin`. Ábralo con cualquier versión de **Unity 6** (si Hub avisa de otra versión, elija la instalada).
2. Al abrirse por primera vez el proyecto:
   - ajusta **Api Compatibility Level = .NET Framework** (necesario para `System.IO.Ports`) y *Run In Background*;
   - genera los materiales y la escena `Assets/Scenes/DamGateTwin.unity` y la abre.
   Si algo no se generó: menú **Digital Twin → Rebuild Dam Scene** (y **Digital Twin → Apply Project Settings**).
3. Pulse **Play**. En el panel izquierdo elija el puerto (`<` `>` / *Actualizar*) y pulse **Conectar**. El Arduino se reinicia al abrir el puerto; los reportes llegan ≈ 2 s después.
4. Sin hardware: botón **Emulador (sin HW)**. Emula el firmware completo (rampa, reportes a 20 Hz, parser, arbitraje) y muestra un potenciómetro virtual con ruido.

Controles de cámara: clic derecho + arrastrar (orbitar), rueda (zoom), `1` vista general, `2` compuerta y chorro, `3` corte del embalse (regla de nivel), `R` reiniciar.

### Flujos de datos

- **Físico → digital:** cada `S:<ángulo>,<fuente>` actualiza el ángulo del gemelo; la compuerta, la rueda del servo, el modelo hidráulico y el deslizador se actualizan solos. El deslizador se mueve con `SetValueWithoutNotify`, por lo que **las actualizaciones del dispositivo nunca se reenvían como comandos**.
- **Digital → físico:** solo cuando el usuario mueve el deslizador se envía `A:<ángulo>` (máximo uno cada 50 ms y siempre el último valor).
- **Sin bloqueo:** el puerto se lee en un hilo de fondo (`ReadLine` con timeout de 50 ms) que entrega líneas completas a una `ConcurrentQueue`; el hilo principal solo las desencola en `Update()`.

### Modelo del embalse

Balance de masa con área superficial *A* = 400 m² (el vaso visual mide 20 m × 20 m):

```
A · dh/dt = Qin − Qc − Qv
Qc = Cd · b · a_ef · √(2g (h − a_ef/2)),   a_ef = min(a, h)      (compuerta de fondo)
Qv = Cw · L · (h − 9)^1.5   si h > 9 m                           (vertedero)
a  = (θ − 0°)/(90° − 0°) · 2 m
```

La simulación permanece en pausa hasta recibir el primer reporte `S:` (sin datos del dispositivo no hay gemelo). Qin = 10 m³/s, b = 3 m, Cd = 0,61, L = 4 m, Cw = 1,7, cresta del vertedero 9 m, corona de la presa 11 m. El tiempo simulado corre ×10 (`timeScale`) para que los cambios de nivel se vean en segundos. Todos los parámetros se editan en el componente `ReservoirModel` del objeto `DigitalTwin`.

Nivel de equilibrio por ángulo (con estos parámetros):

| Ángulo | Apertura | Nivel de equilibrio | Vertedero |
|---|---|---|---|
| 0° | 0 % | 10,29 m | 10,0 m³/s (descarga todo) |
| 15° | 17 % | 9,41 m | 1,8 m³/s |
| 20° | 22 % | 7,93 m | — |
| 30° | 33 % | 3,76 m | — |
| 45° | 50 % | 2,02 m | — |
| 90° | 100 % | 1,45 m | — |

## 4. Experimentos

Pulse **Iniciar registro CSV**. Se crea `UnityProject/DamGateTwin/ExperimentLogs/dam_twin_<fecha>.csv` con columnas:

`time_s, event (S | A | MARK | SETTLED), detail, reported_angle_deg, source, display_angle_deg, last_command_deg, opening_pct, level_m, gate_flow_m3s, spill_flow_m3s, settle_time_s`

- **Experimento A (error angular):** lleve el servo a cinco ángulos del rango útil (p. ej. 0°, 20°, 45°, 70°, 90°), mida con transportador y pulse **Marcar evento** en cada medición; el ángulo de la representación queda en la fila `MARK`. Tres repeticiones por ángulo.
- **Experimento B (coherencia tras conflicto):** alterne diez veces entre perilla y deslizador. `StabilityMonitor` inicia una medición en cada cambio de mando (comando `A:` enviado o transición DIG→POT) y la cierra cuando el ángulo reportado permanece dentro de ±1° durante 0,5 s. El tiempo aparece en el HUD y en las filas `SETTLED`. Repita con las otras políticas para la pregunta de análisis 2.

> Nota para la pregunta 1: el ángulo de `S:` es el **ángulo ordenado** al servo (salida de la rampa), no un ángulo medido. Si el eje se bloquea o el servo no tiene alimentación, la escena mostraría un estado falso; medirlo exigiría realimentación (p. ej. el potenciómetro interno del servo cableado a una entrada analógica, o un encoder).

## 5. Resultados

Los datos de las pruebas están en `Informe/datos/`:

| Archivo | Contenido |
|---|---|
| `expA_hardware.csv` | Experimento A con el Arduino: 5 ángulos × 3 repeticiones. Unity mostró el mismo ángulo que reportó el Arduino en las 15 mediciones (error 0°); tiempo de llegada entre 0,41 s y 1,60 s. |
| `ruido_hardware.csv` | 20 s con la perilla quieta en 45°: 399 reportes, ángulo constante y ningún cambio de mando por ruido. |
| `enlace_hardware.csv` | Frecuencia de reporte medida (20 Hz) y líneas mal formadas (0). |
| `expB_emulador.csv` | Experimento B con el emulador del firmware: 10 alternancias por política. *Último en moverse* obedeció al último mando en 10/10 (estabilización media 0,69 s); *potenciómetro siempre* perdió los 5 comandos digitales y *recogida* no devolvió el mando a la perilla en 4 de 5 casos. |

La posición física del eje no se midió con transportador: el error del experimento A es el que hay entre Unity y el ángulo que reporta el Arduino.
