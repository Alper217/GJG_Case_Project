using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight pooled 2D visual effects for block pops, rocket beams, bomb shockwaves,
/// disco rays, and lock/crate breaks with zero Shader/URP dependencies.
/// </summary>
public class BlastVfxController : MonoBehaviour
{
    private class ActiveFxParticle
    {
        public GameObject Go;
        public SpriteRenderer Renderer;
        public Vector3 Velocity;
        public float Gravity;
        public float Drag;
        public float AngularSpeed;
        public bool EaseOutScale;
        public float Elapsed;
        public float Duration;
        public Vector3 StartScale;
        public Vector3 EndScale;
        public Color StartColor;
        public Color EndColor;
        public bool Active;
    }

    private readonly List<ActiveFxParticle> _pool = new List<ActiveFxParticle>(128);
    private Sprite _softCircleSprite;
    private Sprite _beamQuadSprite;
    private Sprite _ringSprite;
    private Sprite _shardSprite;
    private Sprite _sparkStreakSprite;

    private Camera _shakeCam;
    private Vector3 _cameraBasePos;
    private bool _hasCameraBase;
    private float _shakeElapsed;
    private float _shakeDuration;
    private float _shakeAmplitude;
    private float _shakeSeed;

    private void Awake()
    {
        EnsureProceduralSprites();
    }

    private void OnDisable()
    {
        RestoreCameraFromShake();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            return;
        }

        UpdateCameraShake(dt);

        for (int i = 0; i < _pool.Count; i++)
        {
            ActiveFxParticle p = _pool[i];
            if (!p.Active)
            {
                continue;
            }

            p.Elapsed += dt;
            if (p.Elapsed >= p.Duration)
            {
                p.Active = false;
                p.Go.SetActive(false);
                continue;
            }

            float t = p.Elapsed / p.Duration;
            if (p.Gravity != 0f)
            {
                p.Velocity.y += p.Gravity * dt;
            }

            p.Go.transform.position += p.Velocity * dt;
            if (p.Drag > 0f)
            {
                p.Velocity *= Mathf.Exp(-dt * p.Drag);
            }

            if (p.AngularSpeed != 0f)
            {
                p.Go.transform.Rotate(0f, 0f, p.AngularSpeed * dt);
            }

            float scaleT = p.EaseOutScale ? 1f - (1f - t) * (1f - t) : t;
            p.Go.transform.localScale = Vector3.Lerp(p.StartScale, p.EndScale, scaleT);
            p.Renderer.color = Color.Lerp(p.StartColor, p.EndColor, t * t);
        }
    }

    public void TriggerCameraPunch(float amplitude, float duration = 0.14f)
    {
        Camera cam = Camera.main;
        if (cam == null || amplitude <= 0.001f || duration <= 0.001f)
        {
            return;
        }

        if (!_hasCameraBase || _shakeCam != cam)
        {
            _shakeCam = cam;
            _cameraBasePos = cam.transform.position;
            _hasCameraBase = true;
        }

        _shakeAmplitude = Mathf.Max(_shakeAmplitude * (1f - (_shakeDuration > 0f ? Mathf.Clamp01(_shakeElapsed / _shakeDuration) : 1f)), Mathf.Min(0.12f, amplitude));
        _shakeDuration = Mathf.Clamp(duration, 0.05f, 0.22f);
        _shakeElapsed = 0f;
        _shakeSeed = Random.Range(0f, 100f);
    }

    private void UpdateCameraShake(float dt)
    {
        if (!_hasCameraBase || _shakeCam == null)
        {
            _hasCameraBase = false;
            return;
        }

        _shakeElapsed += dt;
        if (_shakeElapsed >= _shakeDuration)
        {
            RestoreCameraFromShake();
            return;
        }

        float t = _shakeElapsed / _shakeDuration;
        float envelope = (1f - t) * (1f - t);
        float time = _shakeElapsed * 58f + _shakeSeed;
        float ox = (Mathf.PerlinNoise(time, 0.37f) - 0.5f) * 2f * _shakeAmplitude * envelope;
        float oy = (Mathf.PerlinNoise(0.73f, time) - 0.5f) * 2f * _shakeAmplitude * envelope;
        _shakeCam.transform.position = new Vector3(_cameraBasePos.x + ox, _cameraBasePos.y + oy, _cameraBasePos.z);
    }

    private void RestoreCameraFromShake()
    {
        if (_hasCameraBase && _shakeCam != null)
        {
            _shakeCam.transform.position = _cameraBasePos;
        }
        _hasCameraBase = false;
        _shakeAmplitude = 0f;
        _shakeDuration = 0f;
        _shakeElapsed = 0f;
    }

    public void PlayBlockPop(Vector3 worldPos, Color tint, float scaleFactor = 1f)
    {
        EnsureProceduralSprites();

        Color brightTint = Color.Lerp(tint, Color.white, 0.42f);

        // Quick core flash
        SpawnParticle(
            _softCircleSprite,
            worldPos,
            Quaternion.identity,
            Vector3.zero,
            Vector3.one * (0.28f * scaleFactor),
            Vector3.one * (1.05f * scaleFactor),
            Color.Lerp(tint, Color.white, 0.72f),
            new Color(brightTint.r, brightTint.g, brightTint.b, 0f),
            0.15f,
            sortingOrder: 424,
            drag: 0f,
            easeOutScale: true);

        // Crisp expanding halo ring
        SpawnParticle(
            _ringSprite,
            worldPos,
            Quaternion.identity,
            Vector3.zero,
            Vector3.one * (0.26f * scaleFactor),
            Vector3.one * (1.28f * scaleFactor),
            new Color(brightTint.r, brightTint.g, brightTint.b, 0.90f),
            new Color(tint.r, tint.g, tint.b, 0f),
            0.22f,
            sortingOrder: 422,
            drag: 0f,
            easeOutScale: true);

        // 6 spinning colored shards with gravity
        int shardCount = 6;
        for (int i = 0; i < shardCount; i++)
        {
            float baseAngle = (i / (float)shardCount) * Mathf.PI * 2f + Random.Range(-0.32f, 0.32f);
            float speed = Random.Range(2.4f, 4.8f) * scaleFactor;
            Vector3 vel = new Vector3(Mathf.Cos(baseAngle), Mathf.Sin(baseAngle) + 0.35f, 0f) * speed;
            Color chipColor = (i & 1) == 0 ? tint : brightTint;
            SpawnParticle(
                _shardSprite,
                worldPos,
                Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)),
                vel,
                Vector3.one * (Random.Range(0.28f, 0.40f) * scaleFactor),
                Vector3.one * (0.06f * scaleFactor),
                chipColor,
                new Color(chipColor.r, chipColor.g, chipColor.b, 0f),
                0.33f,
                sortingOrder: 426,
                gravity: -9.5f,
                drag: 2.4f,
                angularSpeed: Random.Range(-520f, 520f));
        }

        // 4 hot spark dots
        int sparkCount = 4;
        for (int i = 0; i < sparkCount; i++)
        {
            float angle = (i / (float)sparkCount) * Mathf.PI * 2f + Random.Range(-0.4f, 0.4f);
            Vector3 vel = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * Random.Range(2.8f, 5.2f) * scaleFactor;
            SpawnParticle(
                _softCircleSprite,
                worldPos,
                Quaternion.identity,
                vel,
                Vector3.one * (0.20f * scaleFactor),
                Vector3.one * (0.03f * scaleFactor),
                Color.white,
                new Color(brightTint.r, brightTint.g, brightTint.b, 0f),
                0.20f,
                sortingOrder: 428,
                drag: 3.5f);
        }
    }

    public void PlayCellImpactFlash(Vector3 worldPos, Color tint, float cellWidth, float scaleFactor = 1f)
    {
        EnsureProceduralSprites();

        Color hotTint = Color.Lerp(tint, Color.white, 0.55f);
        SpawnParticle(
            _softCircleSprite,
            worldPos,
            Quaternion.identity,
            Vector3.zero,
            Vector3.one * (cellWidth * 0.32f * scaleFactor),
            Vector3.one * (cellWidth * 1.15f * scaleFactor),
            hotTint,
            new Color(tint.r, tint.g, tint.b, 0f),
            0.16f,
            sortingOrder: 432,
            drag: 0f,
            easeOutScale: true);

        SpawnParticle(
            _ringSprite,
            worldPos,
            Quaternion.identity,
            Vector3.zero,
            Vector3.one * (cellWidth * 0.25f * scaleFactor),
            Vector3.one * (cellWidth * 1.32f * scaleFactor),
            new Color(1f, 0.96f, 0.72f, 0.95f),
            new Color(tint.r, tint.g, tint.b, 0f),
            0.22f,
            sortingOrder: 434,
            drag: 0f,
            easeOutScale: true);
    }

    public void PlayRocketLaunch(Vector3 origin, bool horizontal, bool cross, float cellWidth)
    {
        EnsureProceduralSprites();

        SpawnParticle(
            _softCircleSprite,
            origin,
            Quaternion.identity,
            Vector3.zero,
            Vector3.one * (cellWidth * 0.45f),
            Vector3.one * (cellWidth * 1.85f),
            new Color(1f, 0.98f, 0.78f, 1f),
            new Color(1f, 0.52f, 0.12f, 0f),
            0.20f,
            sortingOrder: 440,
            drag: 0f,
            easeOutScale: true);

        SpawnParticle(
            _ringSprite,
            origin,
            Quaternion.identity,
            Vector3.zero,
            Vector3.one * (cellWidth * 0.30f),
            Vector3.one * (cellWidth * (cross ? 2.4f : 1.9f)),
            new Color(1f, 0.88f, 0.36f, 1f),
            new Color(1f, 0.40f, 0.08f, 0f),
            0.26f,
            sortingOrder: 438,
            drag: 0f,
            easeOutScale: true);

        TriggerCameraPunch(cross ? 0.075f : 0.050f, cross ? 0.15f : 0.12f);
    }

    public void PlayRocketStepProjectile(Vector3 fromWorld, Vector3 toWorld, float stepDuration, float cellWidth)
    {
        EnsureProceduralSprites();

        Vector3 delta = toWorld - fromWorld;
        float dist = delta.magnitude;
        if (dist <= 0.001f)
        {
            return;
        }

        Vector3 dir = delta / dist;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        Quaternion rot = Quaternion.Euler(0f, 0f, angle);
        float dur = Mathf.Max(0.016f, stepDuration);
        Vector3 vel = delta / dur;

        // Glowing fiery wake segment left behind across the swept cell
        Vector3 mid = (fromWorld + toWorld) * 0.5f;
        SpawnParticle(
            _beamQuadSprite,
            mid,
            rot,
            Vector3.zero,
            new Vector3(dist * 1.15f, cellWidth * 0.58f, 1f),
            new Vector3(dist * 1.15f, cellWidth * 0.08f, 1f),
            new Color(1f, 0.90f, 0.38f, 0.95f),
            new Color(1f, 0.38f, 0.08f, 0f),
            0.26f,
            sortingOrder: 435,
            drag: 0f);

        // Moving hot projectile streak racing from current cell to next cell
        SpawnParticle(
            _sparkStreakSprite,
            fromWorld,
            rot,
            vel,
            new Vector3(cellWidth * 1.05f, cellWidth * 0.55f, 1f),
            new Vector3(cellWidth * 1.20f, cellWidth * 0.48f, 1f),
            new Color(1f, 0.98f, 0.82f, 1f),
            new Color(1f, 0.72f, 0.22f, 0.85f),
            dur * 1.15f,
            sortingOrder: 444,
            drag: 0f);

        // Sideways ember sparks tossed off the rocket thruster wake
        Vector3 perp = new Vector3(-dir.y, dir.x, 0f);
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 sparkVel = (perp * side * Random.Range(1.4f, 2.6f) - dir * Random.Range(0.8f, 1.8f));
            SpawnParticle(
                _sparkStreakSprite,
                mid,
                Quaternion.Euler(0f, 0f, Mathf.Atan2(sparkVel.y, sparkVel.x) * Mathf.Rad2Deg),
                sparkVel,
                new Vector3(cellWidth * 0.38f, cellWidth * 0.18f, 1f),
                new Vector3(cellWidth * 0.10f, cellWidth * 0.05f, 1f),
                new Color(1f, 0.86f, 0.30f, 1f),
                new Color(1f, 0.32f, 0.06f, 0f),
                0.22f,
                sortingOrder: 442,
                gravity: -6.5f,
                drag: 2.2f);
        }
    }

    public void PlayRocketBeam(Vector3 origin, bool horizontal, float spanLength, float thickness)
    {
        EnsureProceduralSprites();
        Quaternion rot = horizontal ? Quaternion.identity : Quaternion.Euler(0f, 0f, 90f);
        Vector3 startScale = new Vector3(spanLength * 0.25f, thickness * 0.8f, 1f);
        Vector3 endScale = new Vector3(spanLength * 1.2f, thickness * 0.14f, 1f);

        SpawnParticle(
            _beamQuadSprite,
            origin,
            rot,
            Vector3.zero,
            startScale,
            endScale,
            new Color(1f, 0.90f, 0.35f, 1f),
            new Color(1f, 0.45f, 0.12f, 0f),
            0.30f,
            sortingOrder: 435);

        // Two bright rocket bolts streaking in opposite directions
        Vector3 dir = horizontal ? Vector3.right : Vector3.up;
        float boltSpeed = Mathf.Max(14f, spanLength * 2.4f);
        for (int sign = -1; sign <= 1; sign += 2)
        {
            SpawnParticle(
                _softCircleSprite,
                origin,
                Quaternion.identity,
                dir * (boltSpeed * sign),
                Vector3.one * (thickness * 1.35f),
                Vector3.one * (thickness * 0.45f),
                new Color(1f, 0.95f, 0.65f, 1f),
                new Color(1f, 0.42f, 0.08f, 0f),
                0.28f,
                sortingOrder: 440);
        }
    }

    public void PlayBombDetonationStep(Vector3 origin, float cellWidth, int stage)
    {
        EnsureProceduralSprites();

        if (stage <= 0)
        {
            // Blinding core + fast expanding white-orange shockwave ring + radial streaks
            SpawnParticle(
                _softCircleSprite,
                origin,
                Quaternion.identity,
                Vector3.zero,
                Vector3.one * (cellWidth * 0.55f),
                Vector3.one * (cellWidth * 2.95f),
                new Color(1f, 0.95f, 0.68f, 1f),
                new Color(1f, 0.40f, 0.08f, 0f),
                0.26f,
                sortingOrder: 436,
                drag: 0f,
                easeOutScale: true);

            SpawnParticle(
                _ringSprite,
                origin,
                Quaternion.identity,
                Vector3.zero,
                Vector3.one * (cellWidth * 0.45f),
                Vector3.one * (cellWidth * 3.65f),
                new Color(1f, 0.88f, 0.40f, 1f),
                new Color(1f, 0.28f, 0.06f, 0f),
                0.28f,
                sortingOrder: 440,
                drag: 0f,
                easeOutScale: true);

            int streakCount = 10;
            for (int i = 0; i < streakCount; i++)
            {
                float a = (i / (float)streakCount) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                float speed = Random.Range(4.8f, 7.6f) * Mathf.Max(0.75f, cellWidth);
                SpawnParticle(
                    _sparkStreakSprite,
                    origin + dir * (cellWidth * 0.2f),
                    Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg),
                    dir * speed,
                    new Vector3(cellWidth * 0.65f, cellWidth * 0.26f, 1f),
                    new Vector3(cellWidth * 0.14f, cellWidth * 0.08f, 1f),
                    new Color(1f, 0.92f, 0.48f, 1f),
                    new Color(1f, 0.28f, 0.06f, 0f),
                    0.28f,
                    sortingOrder: 442,
                    gravity: -5f,
                    drag: 3.0f);
            }

            TriggerCameraPunch(0.095f, 0.16f);
        }
        else
        {
            // Outer rolling shock ring for diagonal wave
            SpawnParticle(
                _ringSprite,
                origin,
                Quaternion.identity,
                Vector3.zero,
                Vector3.one * (cellWidth * 1.45f),
                Vector3.one * (cellWidth * 4.35f),
                new Color(1f, 0.62f, 0.16f, 0.95f),
                new Color(0.95f, 0.18f, 0.06f, 0f),
                0.26f,
                sortingOrder: 438,
                drag: 0f,
                easeOutScale: true);
        }
    }

    public void PlayBombShockwave(Vector3 origin, float cellWidth)
    {
        PlayBombDetonationStep(origin, cellWidth, 0);
    }

    public void PlayDiscoChargePulse(Vector3 origin, Color targetColor, float cellWidth)
    {
        EnsureProceduralSprites();
        Color prismGlow = Color.Lerp(targetColor, Color.white, 0.45f);
        SpawnParticle(
            _ringSprite,
            origin,
            Quaternion.identity,
            Vector3.zero,
            Vector3.one * (cellWidth * 1.65f),
            Vector3.one * (cellWidth * 0.35f),
            prismGlow,
            Color.white,
            0.14f,
            sortingOrder: 438,
            drag: 0f);

        SpawnParticle(
            _softCircleSprite,
            origin,
            Quaternion.identity,
            Vector3.zero,
            Vector3.one * (cellWidth * 0.35f),
            Vector3.one * (cellWidth * 1.55f),
            Color.white,
            new Color(targetColor.r, targetColor.g, targetColor.b, 0f),
            0.18f,
            sortingOrder: 440,
            drag: 0f,
            easeOutScale: true);

        TriggerCameraPunch(0.055f, 0.12f);
    }

    public void PlayDiscoRayHit(Vector3 from, Vector3 to, Color beamColor, float cellWidth)
    {
        PlayDiscoRay(from, to, beamColor, cellWidth * 0.30f);
        PlayCellImpactFlash(to, beamColor, cellWidth, 1.1f);
    }

    public void PlayDiscoRay(Vector3 from, Vector3 to, Color beamColor, float thickness)
    {
        EnsureProceduralSprites();
        Vector3 delta = to - from;
        float len = delta.magnitude;
        if (len <= 0.01f)
        {
            return;
        }

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        Vector3 mid = (from + to) * 0.5f;

        SpawnParticle(
            _beamQuadSprite,
            mid,
            Quaternion.Euler(0f, 0f, angle),
            Vector3.zero,
            new Vector3(len, thickness * 0.48f, 1f),
            new Vector3(len, thickness * 0.06f, 1f),
            Color.Lerp(beamColor, Color.white, 0.35f),
            new Color(beamColor.r, beamColor.g, beamColor.b, 0f),
            0.26f,
            sortingOrder: 440);
    }

    private void SpawnParticle(
        Sprite sprite,
        Vector3 pos,
        Quaternion rot,
        Vector3 velocity,
        Vector3 startScale,
        Vector3 endScale,
        Color startColor,
        Color endColor,
        float duration,
        int sortingOrder,
        float gravity = 0f,
        float drag = 4f,
        float angularSpeed = 0f,
        bool easeOutScale = false)
    {
        ActiveFxParticle fx = AcquireParticle();
        fx.Go.transform.position = pos;
        fx.Go.transform.rotation = rot;
        fx.Go.transform.localScale = startScale;
        fx.Renderer.sprite = sprite;
        fx.Renderer.sortingOrder = sortingOrder;
        fx.Renderer.color = startColor;
        fx.Velocity = velocity;
        fx.Gravity = gravity;
        fx.Drag = drag;
        fx.AngularSpeed = angularSpeed;
        fx.EaseOutScale = easeOutScale;
        fx.StartScale = startScale;
        fx.EndScale = endScale;
        fx.StartColor = startColor;
        fx.EndColor = endColor;
        fx.Duration = duration;
        fx.Elapsed = 0f;
        fx.Active = true;
        fx.Go.SetActive(true);
    }

    private ActiveFxParticle AcquireParticle()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            if (!_pool[i].Active)
            {
                return _pool[i];
            }
        }

        var go = new GameObject("BoardFX", typeof(SpriteRenderer));
        go.transform.SetParent(transform, false);
        var p = new ActiveFxParticle
        {
            Go = go,
            Renderer = go.GetComponent<SpriteRenderer>()
        };
        _pool.Add(p);
        return p;
    }

    private void EnsureProceduralSprites()
    {
        if (_softCircleSprite == null)
        {
            const int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[s * s];
            float half = (s - 1) * 0.5f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(1f - dist * dist) * 255f);
                    px[y * s + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _softCircleSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 32f);
        }

        if (_beamQuadSprite == null)
        {
            var tex = new Texture2D(4, 16, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[4 * 16];
            for (int y = 0; y < 16; y++)
            {
                float ny = Mathf.Abs((y / 15f) - 0.5f) * 2f;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(1f - ny * ny) * 255f);
                for (int x = 0; x < 4; x++)
                {
                    px[y * 4 + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _beamQuadSprite = Sprite.Create(tex, new Rect(0, 0, 4, 16), new Vector2(0.5f, 0.5f), 16f);
        }

        if (_ringSprite == null)
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[s * s];
            float half = (s - 1) * 0.5f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    // Hollow crisp ring peaking at r = 0.80
                    float band = 1f - Mathf.Clamp01(Mathf.Abs(dist - 0.80f) / 0.18f);
                    float alpha = band * band;
                    byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                    px[y * s + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 64f);
        }

        if (_shardSprite == null)
        {
            const int s = 16;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[s * s];
            float half = (s - 1) * 0.5f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float nx = Mathf.Abs((x - half) / half);
                    float ny = Mathf.Abs((y - half) / half);
                    // Crisp beveled diamond/tile chip
                    float d = Mathf.Max(nx, ny * 1.15f);
                    float alpha = Mathf.Clamp01((0.92f - d) * 6f);
                    byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                    px[y * s + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _shardSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 16f);
        }

        if (_sparkStreakSprite == null)
        {
            const int w = 32;
            const int h = 12;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            float halfH = (h - 1) * 0.5f;
            for (int y = 0; y < h; y++)
            {
                float ny = Mathf.Abs((y - halfH) / halfH);
                for (int x = 0; x < w; x++)
                {
                    // Head at right (x = 1), tapering tail toward left (x = 0)
                    float nx = x / (float)(w - 1);
                    float tailProfile = nx < 0.78f ? Mathf.Pow(nx / 0.78f, 1.35f) : 1f - ((nx - 0.78f) / 0.22f);
                    float thickness = Mathf.Lerp(0.18f, 1.0f, nx);
                    float vFade = Mathf.Clamp01(1f - (ny / Mathf.Max(0.15f, thickness)) * (ny / Mathf.Max(0.15f, thickness)));
                    float alpha = Mathf.Clamp01(tailProfile * vFade);
                    byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                    px[y * w + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            _sparkStreakSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.75f, 0.5f), 32f);
        }
    }
}
