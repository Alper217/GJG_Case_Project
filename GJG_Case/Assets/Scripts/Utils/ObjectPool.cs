using System.Collections.Generic;
using UnityEngine;

namespace Internal.Core
{
    /// <summary>
    /// A generic object pool for MonoBehaviours.
    /// </summary>
    /// <typeparam name="T"> The type of component to pool. </typeparam>
    public class ObjectPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _parent;
        private readonly Stack<T> _pool;

        public ObjectPool(T prefab, int initialCapacity, Transform parent = null)
        {
            _prefab = prefab;
            _parent = parent;
            _pool = new Stack<T>(initialCapacity);

            for (int i = 0; i < initialCapacity; i++)
            {
                T instance = Object.Instantiate(_prefab, _parent);
                instance.gameObject.SetActive(false);
                _pool.Push(instance);
            }
        }

        public T Get()
        {
            T instance;
            if (_pool.Count > 0)
            {
                instance = _pool.Pop();
            }
            else
            {
                instance = Object.Instantiate(_prefab, _parent);
            }
            
            instance.gameObject.SetActive(true);
            return instance;
        }

        public void Return(T instance)
        {
            instance.gameObject.SetActive(false);
            _pool.Push(instance);
        }
    }
}
