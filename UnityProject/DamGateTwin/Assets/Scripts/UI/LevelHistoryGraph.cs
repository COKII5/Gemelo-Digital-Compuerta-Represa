using UnityEngine;
using UnityEngine.UI;

namespace DamTwin
{
    /// <summary>
    /// Scrolling chart drawn into a texture: reservoir level (blue) and gate opening (orange)
    /// over the last <see cref="windowSeconds"/> seconds, with spillway crest (red) reference.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class LevelHistoryGraph : MonoBehaviour
    {
        public ReservoirModel reservoir;
        public TwinController twin;
        public int width = 360;
        public int height = 130;
        public float windowSeconds = 90f;
        public float sampleInterval = 0.25f;

        private static readonly Color32 Background = new Color32(14, 18, 24, 255);
        private static readonly Color32 Grid = new Color32(40, 48, 58, 255);
        private static readonly Color32 CrestLine = new Color32(220, 60, 50, 255);
        private static readonly Color32 LevelLine = new Color32(70, 165, 245, 255);
        private static readonly Color32 OpeningLine = new Color32(245, 160, 50, 255);

        private Texture2D texture;
        private Color32[] pixels;
        private float[] levels;
        private float[] openings;
        private int count;
        private int head;
        private float timer;

        private void Awake()
        {
            int capacity = Mathf.Max(2, Mathf.CeilToInt(windowSeconds / sampleInterval));
            levels = new float[capacity];
            openings = new float[capacity];
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            pixels = new Color32[width * height];
            GetComponent<RawImage>().texture = texture;
            Redraw();
        }

        private void Update()
        {
            if (reservoir == null || twin == null) return;
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = sampleInterval;

            levels[head] = reservoir.Level;
            openings[head] = twin.DisplayOpening01;
            head = (head + 1) % levels.Length;
            if (count < levels.Length) count++;
            Redraw();
        }

        private void Redraw()
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Background;

            float maxLevel = reservoir != null ? reservoir.damCrest : 11f;
            for (int m = 2; m < maxLevel; m += 2) HLine(ToY(m, maxLevel), Grid);
            if (reservoir != null) HLine(ToY(reservoir.spillwayCrest, maxLevel), CrestLine);

            int capacity = levels.Length;
            int prevXL = -1, prevYL = 0, prevYO = 0;
            for (int i = 0; i < count; i++)
            {
                int index = (head - count + i + capacity) % capacity;
                int x = Mathf.RoundToInt((float)(capacity - count + i) / (capacity - 1) * (width - 1));
                int yl = ToY(levels[index], maxLevel);
                int yo = Mathf.RoundToInt(openings[index] * (height - 1));
                if (prevXL >= 0)
                {
                    Line(prevXL, prevYO, x, yo, OpeningLine);
                    Line(prevXL, prevYL, x, yl, LevelLine);
                }
                prevXL = x;
                prevYL = yl;
                prevYO = yo;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        private int ToY(float value, float max) =>
            Mathf.Clamp(Mathf.RoundToInt(value / max * (height - 1)), 0, height - 1);

        private void HLine(int y, Color32 c)
        {
            int row = y * width;
            for (int x = 0; x < width; x++) pixels[row + x] = c;
        }

        private void Line(int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                Plot(x0, y0, c);
                Plot(x0, y0 + 1, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private void Plot(int x, int y, Color32 c)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            pixels[y * width + x] = c;
        }

        private void OnDestroy()
        {
            if (texture != null) Destroy(texture);
        }
    }
}
