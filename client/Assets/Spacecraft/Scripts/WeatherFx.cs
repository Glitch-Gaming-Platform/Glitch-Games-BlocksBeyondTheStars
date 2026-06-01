using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Weather effects (M27 polish): an IMGUI rain overlay during rain/storm (density scaled by the
    /// authoritative <c>WorldEnvironment.Intensity</c>) and periodic lightning flashes in storms.
    /// Drawn behind the HUD (high GUI.depth), hidden in space. No shaders/particles — robust in builds.
    /// </summary>
    public sealed class WeatherFx : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int Max = 140;
        private readonly float[] _x = new float[Max];
        private readonly float[] _phase = new float[Max];
        private readonly float[] _len = new float[Max];
        private readonly float[] _speed = new float[Max];
        private bool _init;
        private float _flash;
        private float _flashTimer;

        private void Init()
        {
            var rng = new System.Random(7);
            for (int i = 0; i < Max; i++)
            {
                _x[i] = (float)rng.NextDouble();
                _phase[i] = (float)rng.NextDouble();
                _len[i] = 10f + (float)rng.NextDouble() * 18f;
                _speed[i] = 0.8f + (float)rng.NextDouble() * 0.6f;
            }

            _init = true;
        }

        private void Update()
        {
            if (Game?.Environment == null)
            {
                return;
            }

            _flash = Mathf.Max(0f, _flash - Time.deltaTime * 4f);
            if (Game.Environment.Weather == "storm")
            {
                _flashTimer -= Time.deltaTime;
                if (_flashTimer <= 0f)
                {
                    _flashTimer = 6f + Random.value * 10f;
                    _flash = 1f;
                }
            }
        }

        private void OnGUI()
        {
            var env = Game?.Environment;
            if (env == null || Game.SpaceViewActive)
            {
                return;
            }

            bool rain = env.Weather == "rain" || env.Weather == "storm";
            if (!rain && _flash <= 0.01f)
            {
                return;
            }

            if (!_init)
            {
                Init();
            }

            GUI.depth = 10; // behind the HUD
            var prevColor = GUI.color;

            if (rain && !Game.MenuOpen)
            {
                int count = Mathf.RoundToInt(Max * Mathf.Clamp01(0.4f + env.Intensity * 0.6f));
                float h = Screen.height, w = Screen.width, t = Time.time;
                GUI.color = new Color(0.6f, 0.8f, 1f, 0.45f);
                for (int i = 0; i < count; i++)
                {
                    float y = ((_phase[i] + t * _speed[i]) % 1f) * (h + 40f) - 20f;
                    float x = _x[i] * w + Mathf.Sin(t + i) * 5f;
                    GUI.DrawTexture(new Rect(x, y, 2f, _len[i]), Texture2D.whiteTexture);
                }
            }

            if (_flash > 0.01f)
            {
                GUI.color = new Color(0.82f, 0.9f, 1f, _flash * 0.35f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            }

            GUI.color = prevColor;
        }
    }
}
