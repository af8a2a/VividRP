// Copyright (c) Jason Ma

using System;
using System.Collections.Generic;
using UnityEngine;

namespace LWGUI.Runtime.LwguiGradient
{
    public class LwguiMergedColorCurves : IDisposable
    {
        public List<List<LwguiGradient.LwguiKeyframe>> curves = new ();

        public LwguiMergedColorCurves()
        {
            for (int c = 0; c < (int)LwguiGradient.Channel.Num; c++)
                curves.Add(new List<LwguiGradient.LwguiKeyframe>());
        }

        public LwguiMergedColorCurves(List<AnimationCurve> rgbaCurves)
        {
            for (int c = 0; c < (int)LwguiGradient.Channel.Num; c++)
                curves.Add(new List<LwguiGradient.LwguiKeyframe>());
                
            // Get color keys
            {
                var timeColorDic = new Dictionary<float, List<(float value, int index)>>();
                for (int c = 0; c < (int)LwguiGradient.Channel.Num - 1; c++)
                {
                    var keys = rgbaCurves[c].keys;
                    for (int j = 0; j < keys.Length; j++)
                    {
                        var keyframe = keys[j];
                        if (timeColorDic.ContainsKey(keyframe.time))
                        {
                            timeColorDic[keyframe.time].Add((keyframe.value, j));
                        }
                        else
                        {
                            timeColorDic.Add(keyframe.time, new List<(float value, int index)> { (keyframe.value, j) });
                        }
                    }
                }

                foreach (var kwPair in timeColorDic)
                {
                    if (kwPair.Value.Count == (int)LwguiGradient.Channel.Num - 1)
                    {
                        for (int c = 0; c < (int)LwguiGradient.Channel.Num - 1; c++)
                        {
                            curves[c].Add(new LwguiGradient.LwguiKeyframe(kwPair.Key, kwPair.Value[c].value, kwPair.Value[c].index));
                        }
                    }
                }
            }
            
            // Get alpha keys
            for (int i = 0; i < rgbaCurves[(int)LwguiGradient.Channel.Alpha].keys.Length; i++)
            {
                var alphaKey = rgbaCurves[(int)LwguiGradient.Channel.Alpha].keys[i];
                curves[(int)LwguiGradient.Channel.Alpha].Add(new LwguiGradient.LwguiKeyframe(alphaKey.time, alphaKey.value, i));
            }
        }

        public LwguiMergedColorCurves(Gradient gradient)
        {
            for (int c = 0; c < (int)LwguiGradient.Channel.Num; c++)
                curves.Add(new List<LwguiGradient.LwguiKeyframe>());

            foreach (var colorKey in gradient.colorKeys)
            {
                for (int c = 0; c < (int)LwguiGradient.Channel.Num - 1; c++)
                {
                    curves[c].Add(new LwguiGradient.LwguiKeyframe(colorKey.time, colorKey.color[c], 0));
                }
            }
            foreach (var alphaKey in gradient.alphaKeys)
            {
                curves[(int)LwguiGradient.Channel.Alpha].Add(new LwguiGradient.LwguiKeyframe(alphaKey.time, alphaKey.alpha, 0));
            }
        }

        public Gradient ToGradient(int maxGradientKeyCount = 8)
        {
            var rKeys = curves[(int)LwguiGradient.Channel.Red];
            var gKeys = curves[(int)LwguiGradient.Channel.Green];
            var bKeys = curves[(int)LwguiGradient.Channel.Blue];
            var aKeys = curves[(int)LwguiGradient.Channel.Alpha];

            var colorCount = Math.Min(Math.Min(rKeys.Count, Math.Min(gKeys.Count, bKeys.Count)), maxGradientKeyCount);
            var alphaCount = Math.Min(aKeys.Count, maxGradientKeyCount);

            var colorKeys = new GradientColorKey[colorCount];
            for (int i = 0; i < colorCount; i++)
            {
                colorKeys[i] = new GradientColorKey(new Color(rKeys[i].value, gKeys[i].value, bKeys[i].value), rKeys[i].time);
            }

            var alphaKeys = new GradientAlphaKey[alphaCount];
            for (int i = 0; i < alphaCount; i++)
            {
                alphaKeys[i] = new GradientAlphaKey(aKeys[i].value, aKeys[i].time);
            }

            var gradient = new Gradient
            {
                colorKeys = colorKeys,
                alphaKeys = alphaKeys
            };
            return gradient;
        }

        public List<AnimationCurve> ToAnimationCurves()
        {
            var outCurves = new List<AnimationCurve>();
            for (int c = 0; c < (int)LwguiGradient.Channel.Num; c++)
            {
                var curve = new AnimationCurve();
                foreach (var key in curves[c])
                {
                    curve.AddKey(new Keyframe(key.time, key.value).SetLinearTangentMode());
                }
                curve.SetLinearTangents();
                outCurves.Add(curve);
            }

            return outCurves;
        }

        public LwguiGradient ToLwguiGradient()
        {
            return new LwguiGradient(ToAnimationCurves());
        }

        public void Dispose()
        {
            curves?.Clear();
        }
    }
}