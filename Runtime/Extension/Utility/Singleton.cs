using System;

namespace UnityEngine.Rendering.Universal
{
    public class Singleton<T> where T : new()
    {
        static T s_instance;

        public static T instance
        {
            get
            {
                if (s_instance == null)
                {
                    s_instance = new T();
                }

                return s_instance;
            }
        }
    }


    public class LazySingleton<T> where T : new()
    {
        static Lazy<T> s_instance => new(() => new T());

        public static T instance => s_instance.Value;
    }

}