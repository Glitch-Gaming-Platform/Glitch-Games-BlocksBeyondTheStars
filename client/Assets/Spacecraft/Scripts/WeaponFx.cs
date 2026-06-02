using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Tool/weapon visual effects (M27 polish): a beam/tracer + muzzle flash + impact spark burst when
    /// the player attacks, and spark showers while drilling. Code-built from cubes on the always-included
    /// Unlit/Color shader — no assets, no new shader. Render-only; combat stays server-authoritative.
    /// </summary>
    public sealed class WeaponFx : MonoBehaviour
    {
        /// <summary>Fires a shot effect: a fading beam from the muzzle to the target, a muzzle flash and
        /// an impact spark burst (works for ranged tracers and short melee strikes alike).</summary>
        public void Shoot(Vector3 from, Vector3 to, Color color)
        {
            float len = Vector3.Distance(from, to);
            if (len > 0.05f)
            {
                var beam = Cube("Beam", (from + to) * 0.5f, new Vector3(0.05f, 0.05f, len), color);
                beam.transform.rotation = Quaternion.LookRotation(to - from);
                beam.AddComponent<FadeKill>().Life = 0.12f;
            }

            Flash(from, color, 0.28f);
            Sparks(to, color, 8);
        }

        /// <summary>A brief muzzle/impact flash (a quickly-shrinking bright cube).</summary>
        public void Flash(Vector3 at, Color color, float size)
        {
            var f = Cube("Flash", at, Vector3.one * size, color);
            var fk = f.AddComponent<FadeKill>();
            fk.Life = 0.1f;
            fk.Shrink = true;
        }

        /// <summary>A small shower of spark particles at a point (impacts, drilling).</summary>
        public void Sparks(Vector3 at, Color color, int count = 5)
        {
            for (int i = 0; i < count; i++)
            {
                var p = Cube("Spark", at, Vector3.one * 0.07f, color);
                var s = p.AddComponent<Spark>();
                s.Vel = new Vector3(Random.Range(-2.2f, 2.2f), Random.Range(0.5f, 3f), Random.Range(-2.2f, 2.2f));
            }
        }

        /// <summary>An expanding horizontal ring of bits — a scanner ping.</summary>
        public void Pulse(Vector3 at, Color color)
        {
            const int n = 16;
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                var p = Cube("Pulse", at, Vector3.one * 0.08f, color);
                var s = p.AddComponent<Spark>();
                s.Vel = new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)) * 4.2f;
                s.Gravity = false;
                s.LifeOverride = 0.45f;
            }
        }

        /// <summary>A low dust puff (landing / footfall).</summary>
        public void Dust(Vector3 at, int count = 7)
        {
            for (int i = 0; i < count; i++)
            {
                var p = Cube("Dust", at + Vector3.up * 0.05f, Vector3.one * 0.1f, new Color(0.62f, 0.57f, 0.47f));
                var s = p.AddComponent<Spark>();
                s.Vel = new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(0.3f, 1.2f), Random.Range(-1.5f, 1.5f));
            }
        }

        private static GameObject Cube(string name, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mat(color);
            return go;
        }

        private static Material Mat(Color c)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Spacecraft/VertexColorOpaque");
            return new Material(shader) { color = c };
        }

        /// <summary>Fades/shrinks an effect cube out over a short life, then destroys it.</summary>
        private sealed class FadeKill : MonoBehaviour
        {
            public float Life = 0.12f;
            public bool Shrink;
            private float _t;
            private Vector3 _scale0;

            private void Start() => _scale0 = transform.localScale;

            private void Update()
            {
                _t += Time.deltaTime;
                if (Shrink)
                {
                    transform.localScale = _scale0 * Mathf.Max(0f, 1f - _t / Life);
                }

                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }

        /// <summary>A short-lived spark: arcs out under gravity, shrinks, self-destroys.</summary>
        private sealed class Spark : MonoBehaviour
        {
            public Vector3 Vel;
            public bool Gravity = true;
            public float LifeOverride;
            private float _life = 0.45f;
            private float _t;
            private Vector3 _scale0;

            private void Start()
            {
                _scale0 = transform.localScale;
                if (LifeOverride > 0f)
                {
                    _life = LifeOverride;
                }
            }

            private void Update()
            {
                _t += Time.deltaTime;
                if (Gravity)
                {
                    Vel += Vector3.down * 9f * Time.deltaTime;
                }

                transform.position += Vel * Time.deltaTime;
                transform.localScale = _scale0 * Mathf.Max(0f, 1f - _t / _life);
                if (_t >= _life)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
