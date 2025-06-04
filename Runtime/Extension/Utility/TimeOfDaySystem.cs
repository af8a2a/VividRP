
using UnityEngine;

namespace UnityEngine.Rendering.Universal
{
    public sealed class TimeOfDaySystem
    {
        /// <summary>
        /// Get time of day from directional light.
        /// </summary>
        /// <param name="light"></param>
        /// <returns>Time in range dayLength.</returns>
        public static float GetTimeOfDayFromLight(Light light, float dayLength = 1.0f)
        {
            if (light == null)
                return 0.0f;

            float timeOfDay = light.transform.forward.z > 0 ? -light.transform.forward.y + 1.0f : light.transform.forward.y + 3.0f;
            timeOfDay /= 4.0f;

            return timeOfDay * dayLength;
        }
    }

}
