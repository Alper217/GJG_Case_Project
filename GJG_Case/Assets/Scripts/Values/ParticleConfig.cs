using UnityEngine;
using System.Collections.Generic;

namespace Internal.Values
{
    [CreateAssetMenu(fileName = "ParticleConfig", menuName = "Game/ParticleConfig")]
    public class ParticleConfig : ScriptableObject
    {
        [System.Serializable]
        public struct ColorParticles
        {
            public ParticleSystem ParticlePrefab;
        }

        public List<ColorParticles> Particles;
    }
}
