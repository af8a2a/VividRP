using System;
using System.Collections.Generic;
using Unity.Collections;

namespace UnityEngine.Rendering.Universal
{
    public static class LightManager
    {
        static List<Light> m_DirectionalLights = new List<Light>();
        static List<Light> m_PointLights = new List<Light>();
        static List<Light> m_SpotLights = new List<Light>();
        static List<Light> m_AreaLight = new List<Light>();

        public static List<Light> DirectionalLights => m_DirectionalLights;
        public static List<Light> SpotLights => m_SpotLights;
        public static List<Light> PointLights => m_PointLights;
        public static List<Light> AreaLight => m_AreaLight;

        
       internal static void OnLightEnable(Light light)
        {
            switch (light.type)
            {
                case LightType.Spot:
                    m_SpotLights.Add(light);
                    break;
                case LightType.Directional:
                    m_DirectionalLights.Add(light);
                    break;
                case LightType.Rectangle:
                    m_AreaLight.Add(light);
                    break;
                case LightType.Point:
                    m_PointLights.Add(light);
                    break;
                default:
                    throw new NotSupportedException("Not supported yet");
            }
        }

        internal static void OnLightDisable(Light light)
        {
            switch (light.type)
            {
                case LightType.Spot:
                    m_SpotLights.Remove(light);
                    break;
                case LightType.Directional:
                    m_DirectionalLights.Remove(light);
                    break;
                case LightType.Rectangle:
                    m_AreaLight.Remove(light);
                    break;
                case LightType.Point:
                    m_PointLights.Remove(light);
                    break;
                default:
                    throw new NotSupportedException("Not supported yet");
            }

        }
    }
}