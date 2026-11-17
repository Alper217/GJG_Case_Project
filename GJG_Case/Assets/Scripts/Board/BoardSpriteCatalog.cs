using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Holds all block color variants (Default, A/Rocket-hint, B/Bomb-hint, C/Disco-hint)
/// plus 2.5D booster and obstacle sprites used by BoardController and GoalsController.
/// </summary>
[Serializable]
public class BoardSpriteCatalog
{
    [Serializable]
    public struct ColorSpriteSet
    {
        public BlockColor Color;
        public Sprite DefaultSprite;
        public Sprite TierASprite;
        public Sprite TierBSprite;
        public Sprite TierCSprite;
        public Color BlastParticleColor;
    }

    [Header("6 Playable Colors (Match_Items_internship)")]
    [SerializeField] private ColorSpriteSet[] _colorSets = new ColorSpriteSet[6];

    [Header("Spawned 2.5D Booster Sprites")]
    [SerializeField] private Sprite _rocketSprite;
    [SerializeField] private Sprite _bombSprite;
    [SerializeField] private Sprite _discoSprite;

    [Header("2.5D Obstacle Sprites")]
    [SerializeField] private Sprite _woodCrateSprite;
    [SerializeField] private Sprite _lockOverlaySprite;

    public Sprite RocketSprite => _rocketSprite;
    public Sprite BombSprite => _bombSprite;
    public Sprite DiscoSprite => _discoSprite;
    public Sprite WoodCrateSprite => _woodCrateSprite;
    public Sprite LockOverlaySprite => _lockOverlaySprite;

    public Sprite GetBlockSprite(BlockColor color, int groupSize, int rocketThreshold, int bombThreshold, int discoThreshold)
    {
        if (!TryGetColorSet(color, out ColorSpriteSet set))
        {
            return null;
        }

        if (groupSize >= discoThreshold && set.TierCSprite != null)
        {
            return set.TierCSprite;
        }

        if (groupSize >= bombThreshold && set.TierBSprite != null)
        {
            return set.TierBSprite;
        }

        if (groupSize >= rocketThreshold && set.TierASprite != null)
        {
            return set.TierASprite;
        }

        return set.DefaultSprite;
    }

    public Sprite GetBoosterSprite(BoosterType booster)
    {
        return booster switch
        {
            BoosterType.Rocket => _rocketSprite,
            BoosterType.Bomb => _bombSprite,
            BoosterType.Disco => _discoSprite,
            _ => null
        };
    }

    public Sprite GetGoalIconSprite(GoalTargetType target)
    {
        return target switch
        {
            GoalTargetType.WoodCrate => _woodCrateSprite,
            GoalTargetType.LockedBlock => _lockOverlaySprite,
            _ => TryGetColorSet((BlockColor)(int)target, out ColorSpriteSet set) ? set.DefaultSprite : null
        };
    }

    public Color GetBlastColor(BlockColor color)
    {
        return TryGetColorSet(color, out ColorSpriteSet set) ? set.BlastParticleColor : Color.white;
    }

    public Sprite GetFirstValidSampleSprite()
    {
        if (_colorSets != null)
        {
            for (int i = 0; i < _colorSets.Length; i++)
            {
                if (_colorSets[i].DefaultSprite != null)
                {
                    return _colorSets[i].DefaultSprite;
                }
            }
        }

        return _rocketSprite;
    }

    private bool TryGetColorSet(BlockColor color, out ColorSpriteSet result)
    {
        int idx = (int)color;
        if (_colorSets != null && idx >= 0 && idx < _colorSets.Length && _colorSets[idx].Color == color)
        {
            result = _colorSets[idx];
            return true;
        }

        if (_colorSets != null)
        {
            for (int i = 0; i < _colorSets.Length; i++)
            {
                if (_colorSets[i].Color == color)
                {
                    result = _colorSets[i];
                    return true;
                }
            }
        }

        result = default;
        return false;
    }

#if UNITY_EDITOR
    public void LoadDefaultsInEditor()
    {
        string[] colorNames = { "Blue", "Green", "Pink", "Purple", "Red", "Yellow" };
        Color[] particleColors =
        {
            new Color(0.20f, 0.72f, 1.00f, 1f),
            new Color(0.45f, 0.85f, 0.22f, 1f),
            new Color(1.00f, 0.42f, 0.82f, 1f),
            new Color(0.65f, 0.28f, 0.96f, 1f),
            new Color(0.95f, 0.24f, 0.20f, 1f),
            new Color(1.00f, 0.80f, 0.14f, 1f)
        };

        _colorSets = new ColorSpriteSet[6];
        for (int i = 0; i < 6; i++)
        {
            string prefix = $"Assets/Match_Items_internship/{colorNames[i]}";
            _colorSets[i] = new ColorSpriteSet
            {
                Color = (BlockColor)i,
                DefaultSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{prefix}_Default.png"),
                TierASprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{prefix}_A.png"),
                TierBSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{prefix}_B.png"),
                TierCSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{prefix}_C.png"),
                BlastParticleColor = particleColors[i]
            };
        }

        _rocketSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Board/Booster_Rocket.png");
        _bombSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Board/Booster_Bomb.png");
        _discoSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Board/Booster_Disco.png");
        _woodCrateSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Board/Obstacle_Crate.png");
        _lockOverlaySprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Board/Obstacle_Lock.png");
    }
#endif
}
