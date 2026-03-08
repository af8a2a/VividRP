// Copyright (c) Jason Ma

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LWGUI.Runtime.LwguiGradient
{
    [Serializable]
    public class LwguiGradient : IDisposable
    {
        #region Channel Enum
        
        public enum Channel
        {
            Red     = 0,
            Green   = 1,
            Blue    = 2,
            Alpha   = 3,
            Num     = 4
        }

        [Flags] // Flags Attribute must be used to support bit operations
        public enum ChannelMask
        {
            None    = 0,
            Red     = 1 << 0,
            Green   = 1 << 1,
            Blue    = 1 << 2,
            Alpha   = 1 << 3,
            RGB     = Red | Green | Blue,
            All     = ~0
        }
        
        public enum GradientTimeRange
        {
            One                 = 1,
            TwentyFour          = 24,
            TwentyFourHundred   = 2400
        }

        public static bool HasChannelMask(ChannelMask channelMaskA, ChannelMask channelMaskB) => ((uint)channelMaskA & (uint)channelMaskB) > 0;
        
        public static bool IsChannelIndexInMask(int channelIndex, ChannelMask channelMask) => ((uint)channelMask & (uint)(1 << channelIndex)) > 0;
        
        public static ChannelMask ChannelIndexToMask(int channelIndex) => (ChannelMask)(1 << channelIndex);
        
        #endregion

        #region Const

        public static readonly Color[] channelColors = { Color.red, Color.green, Color.blue, Color.white };
        public static readonly char[] channelNames = { 'r', 'g', 'b', 'a' };

        public static AnimationCurve defaultCurve => new (new Keyframe(0, 1).SetLinearTangentMode(), new Keyframe(1, 1).SetLinearTangentMode());

        #endregion

        #region Data

        // The complete data is stored by RGBA Curves and can be converted into Texture
        [SerializeField] private List<AnimationCurve> _curves;
        
        // Reusable buffer to reduce per-call allocations when generating preview pixels
        [NonSerialized] private static Color[] _pixelsCache;
        [NonSerialized] private static int _pixelsCacheWidth;
        [NonSerialized] private static int _pixelsCacheHeight;
        
        public List<AnimationCurve> rawCurves
        {
            get
            {
                _curves ??= new List<AnimationCurve>();
                if (_curves.Count < (int)Channel.Num)
                {
                    for (int c = 0; c < (int)Channel.Num; c++)
                    {
                        if (c == _curves.Count)
                            _curves.Add(defaultCurve);
                    }
                }

                return _curves;
            }
            set => SetRgbaCurves(value);
        }

        public AnimationCurve redCurve
        {
            get => rawCurves[(int)Channel.Red] ?? defaultCurve;
            set => SetCurve(value, ChannelMask.Red);
        }

        public AnimationCurve greenCurve
        {
            get => rawCurves[(int)Channel.Green] ?? defaultCurve;
            set => SetCurve(value, ChannelMask.Green);
        }

        public AnimationCurve blueCurve
        {
            get => rawCurves[(int)Channel.Blue] ?? defaultCurve;
            set => SetCurve(value, ChannelMask.Blue);
        }

        public AnimationCurve alphaCurve
        {
            get => rawCurves[(int)Channel.Alpha] ?? defaultCurve;
            set => SetCurve(value, ChannelMask.Alpha);
        }

        #endregion

        #region Constructor

        public LwguiGradient()
        {
            _curves = new List<AnimationCurve>();
            for (int c = 0; c < (int)Channel.Num; c++)
                _curves.Add(defaultCurve);
        }

        public LwguiGradient(LwguiGradient src)
        {
            DeepCopyFrom(src);
        }

        public LwguiGradient(params Keyframe[] keys)
        {
            _curves = new List<AnimationCurve>();
            for (int c = 0; c < (int)Channel.Num; c++)
                _curves.Add(new AnimationCurve());
            
            if (keys?.Length > 0)
            {
                AddKeys(keys, ChannelMask.All);
            }
        }

        public LwguiGradient(Color[] colors, float[] times)
        {
            _curves = new List<AnimationCurve>();
            for (int c = 0; c < (int)Channel.Num; c++)
                _curves.Add(new AnimationCurve());
            
            if (colors == null || times == null)
                return;

            for (int i = 0; i < Mathf.Min(colors.Length, times.Length); i++)
            {
                for (int c = 0; c < (int)Channel.Num; c++)
                {
                    _curves[c].AddKey(new Keyframe(times[i], colors[i][c]).SetLinearTangentMode());
                }
            }
            SetLinearTangentMode();
        }

        public LwguiGradient(List<AnimationCurve> inRgbaCurves) => SetRgbaCurves(inRgbaCurves);

        public static LwguiGradient white   => new ();

        public static LwguiGradient gray    => new (new []{Color.gray, Color.gray}, new []{0.0f, 1.0f});

        public static LwguiGradient black   => new (new []{Color.black, Color.black}, new []{0.0f, 1.0f});

        public static LwguiGradient red     => new (new []{Color.red, Color.red}, new []{0.0f, 1.0f});

        public static LwguiGradient green   => new (new []{Color.green, Color.green}, new []{0.0f, 1.0f});

        public static LwguiGradient blue    => new (new []{Color.blue, Color.blue}, new []{0.0f, 1.0f});

        public static LwguiGradient cyan    => new (new []{Color.cyan, Color.cyan}, new []{0.0f, 1.0f});

        public static LwguiGradient magenta => new (new []{Color.magenta, Color.magenta}, new []{0.0f, 1.0f});

        public static LwguiGradient yellow  => new (new []{Color.yellow, Color.yellow}, new []{0.0f, 1.0f});

        #endregion

        public int GetValueBasedHashCode()
        {
            var hash = 17;

            if (_curves != null)
            {
                for (int i = 0; i < _curves.Count; i++)
                {
                    var curve = _curves[i];
                    if (curve != null)
                    {
                        hash = hash * 23 + curve.GetHashCode();
                    }
                }
            }

            return hash;
        }
        
        public void Dispose()
        {
            _curves?.Clear();
            _pixelsCache = null;
            _pixelsCacheWidth = _pixelsCacheHeight = 0;
        }
        
        public void Clear(ChannelMask channelMask = ChannelMask.All)
        {
            _curves ??= new List<AnimationCurve>();
            for (int c = 0; c < (int)Channel.Num; c++)
            {
                if (!IsChannelIndexInMask(c, channelMask)) continue;
                
                if (_curves.Count > c) _curves[c].keys = Array.Empty<Keyframe>();
                else _curves.Add(new AnimationCurve());
            }
        }
        
        public void DeepCopyFrom(LwguiGradient src)
        {
            _curves ??= new List<AnimationCurve>();
            for (int c = 0; c < (int)Channel.Num; c++)
            {
                if (_curves.Count == c)
                    _curves.Add(new AnimationCurve());

                _curves[c].keys = Array.Empty<Keyframe>();
            }

            for (int c = 0; c < src._curves.Count; c++)
            {
                var keys = src._curves[c].keys;
                for (int k = 0; k < keys.Length; k++)
                {
                    _curves[c].AddKey(keys[k]);
                }
            }
        }

        public void SetCurve(AnimationCurve curve, ChannelMask channelMask)
        {
            curve ??= defaultCurve;
            for (int c = 0; c < (int)Channel.Num; c++)
            {
                if (!IsChannelIndexInMask(c, channelMask)) continue;

                _curves[c] = curve;
            }
        }
        
        public void SetRgbaCurves(List<AnimationCurve> inRgbaCurves)
        {
            _curves = new List<AnimationCurve>();
            
            for (int c = 0; c < (int)Channel.Num; c++)
            {
                if (inRgbaCurves?.Count > c && inRgbaCurves[c]?.keys.Length > 0)
                {
                    _curves.Add(inRgbaCurves[c]);
                }
                else
                {
                    _curves.Add(defaultCurve);
                }
            }
        }

        public void AddKey(Keyframe key, ChannelMask channelMask)
        {
            for (int c = 0; c < (int)Channel.Num; c++)
            {
                if (!IsChannelIndexInMask(c, channelMask))
                    continue;
                
                rawCurves[c].AddKey(key);
            }

        }

        public void AddKeys(Keyframe[] keys, ChannelMask channelMask)
        {
            for (int i = 0; i < keys?.Length; i++)
            {
                AddKey(keys[i], channelMask);
            }
        }

        public Color Evaluate(float time, ChannelMask channelMask = ChannelMask.All, GradientTimeRange timeRange = GradientTimeRange.One)
        {
            switch (timeRange)
            {
                case GradientTimeRange.One:                                         break;
                case GradientTimeRange.TwentyFour:        time *= 1.0f / 24.0f;     break;
                case GradientTimeRange.TwentyFourHundred: time *= 1.0f / 2400.0f;   break;
                default:                                  throw new ArgumentOutOfRangeException(nameof(timeRange), timeRange, null);
            }
            
            if (channelMask == ChannelMask.Alpha)
            {
                var alpha = rawCurves[(int)Channel.Alpha].Evaluate(time);
                return new Color(alpha, alpha, alpha, 1);
            }

            // Fast paths to avoid redundant mask checks and alpha evaluation
            if (channelMask == ChannelMask.All)
            {
                return new Color(
                    rawCurves[(int)Channel.Red].Evaluate(time),
                    rawCurves[(int)Channel.Green].Evaluate(time),
                    rawCurves[(int)Channel.Blue].Evaluate(time),
                    rawCurves[(int)Channel.Alpha].Evaluate(time));
            }
            if (channelMask == ChannelMask.RGB)
            {
                return new Color(
                    rawCurves[(int)Channel.Red].Evaluate(time),
                    rawCurves[(int)Channel.Green].Evaluate(time),
                    rawCurves[(int)Channel.Blue].Evaluate(time),
                    1);
            }

            return new Color(
                 IsChannelIndexInMask((int)Channel.Red, channelMask)     ? rawCurves[(int)Channel.Red].Evaluate(time)     : 0,
                 IsChannelIndexInMask((int)Channel.Green, channelMask)   ? rawCurves[(int)Channel.Green].Evaluate(time)   : 0,
                 IsChannelIndexInMask((int)Channel.Blue, channelMask)    ? rawCurves[(int)Channel.Blue].Evaluate(time)    : 0,
                 IsChannelIndexInMask((int)Channel.Alpha, channelMask)   ? rawCurves[(int)Channel.Alpha].Evaluate(time)   : 1);
        }

        public void SetLinearTangentMode()
        {
            for (int c = 0; c < (int)Channel.Num; c++)
            {
                rawCurves[c].SetLinearTangents();
            }
        }

        #region LwguiGradient <=> Ramp Texture

        public Color[] GetPixels(int width, int height, ChannelMask channelMask = ChannelMask.All)
        {
            var pixels = new Color[width * height];
            FillPixelsNonAlloc(pixels, width, height, channelMask);
            return pixels;
        }

        public void GetPixels(ref Color[] outputPixels, ref int currentIndex, int width, int height, ChannelMask channelMask = ChannelMask.All)
        {
            if (outputPixels == null || currentIndex >= outputPixels.Length)
                return;
            
            var invWidth = 1.0f / width;
            for (var x = 0; x < width; x++)
            {
                var u   = x * invWidth;
                var col = Evaluate(u, channelMask);
                for (int i = 0; i < height; i++)
                {
                    if (currentIndex < outputPixels.Length)
                        outputPixels[currentIndex ++] = col;
                }
            }
        }

        // Non-alloc helper to fill a provided buffer with gradient pixels
        private void FillPixelsNonAlloc(Color[] buffer, int width, int height, ChannelMask channelMask = ChannelMask.All)
        {
            if (buffer == null || buffer.Length < width * height) return;
            var invWidth = 1.0f / width;
            int rowStride = width;
            for (int x = 0; x < width; x++)
            {
                var u = x * invWidth;
                var col = Evaluate(u, channelMask);
                int idx = x;
                for (int y = 0; y < height; y++)
                {
                    buffer[idx] = col;
                    idx += rowStride;
                }
            }
        }

        private void EnsurePixelCache(int width, int height)
        {
            if (_pixelsCache == null || _pixelsCacheWidth != width || _pixelsCacheHeight != height)
            {
                _pixelsCache = new Color[width * height];
                _pixelsCacheWidth = width;
                _pixelsCacheHeight = height;
            }
        }

        public Texture2D GetPreviewRampTexture(int width = 256, int height = 1, ColorSpace colorSpace = ColorSpace.Gamma, ChannelMask channelMask = ChannelMask.All)
        {
            if (LwguiGradientHelper.TryGetRampPreview(this, width, height, colorSpace, channelMask, out var cachedPreview)) 
                return cachedPreview;
            
            var rampPreview = new Texture2D(width, height, TextureFormat.RGBA32, false, colorSpace == ColorSpace.Linear);
            EnsurePixelCache(width, height);
            FillPixelsNonAlloc(_pixelsCache, width, height, channelMask);
            rampPreview.SetPixels(_pixelsCache);
            rampPreview.wrapMode = TextureWrapMode.Clamp;
            rampPreview.name = "LWGUI Gradient Preview";
            rampPreview.Apply();
            
            LwguiGradientHelper.SetRampPreview(this, width, height, colorSpace, channelMask, rampPreview);
            return rampPreview;
        }

        #endregion

        #region LwguiGradient <=> Gradient
        
        public struct LwguiKeyframe
        {
            public float time;
            public float value;
            public int index;

            public LwguiKeyframe(float time, float value, int index)
            {
                this.time  = time;
                this.value = value;
                this.index = index;
            }
        }

        public static LwguiGradient FromGradient(Gradient gradient)
        {
            if (gradient == null)
                return new LwguiGradient();

            var curves = new List<AnimationCurve>((int)Channel.Num);
            for (int c = 0; c < (int)Channel.Num; c++)
                curves[c] = new AnimationCurve();

            var colorKeys = gradient.colorKeys;
            for (int i = 0; i < colorKeys.Length; i++)
            {
                var ck = colorKeys[i];
                curves[(int)Channel.Red]  .AddKey(new Keyframe(ck.time, ck.color.r).SetLinearTangentMode());
                curves[(int)Channel.Green].AddKey(new Keyframe(ck.time, ck.color.g).SetLinearTangentMode());
                curves[(int)Channel.Blue] .AddKey(new Keyframe(ck.time, ck.color.b).SetLinearTangentMode());
            }

            var alphaKeys = gradient.alphaKeys;
            for (int i = 0; i < alphaKeys.Length; i++)
            {
                var ak = alphaKeys[i];
                curves[(int)Channel.Alpha].AddKey(new Keyframe(ak.time, ak.alpha).SetLinearTangentMode());
            }

            for (int c = 0; c < (int)Channel.Num; c++)
                curves[c].SetLinearTangents();

            return new LwguiGradient(curves);
        }

        /// Warning: This is a lossy conversion and will ignore keys that are not aligned with the RGB channels
        public Gradient ToGradient(int maxGradientKeyCount = 8)
        {
            if (rawCurves == null || rawCurves.Count < (int)Channel.Num)
            {
                return new Gradient();
            }

            var redKeys = rawCurves[(int)Channel.Red].keys;
            var greenKeys = rawCurves[(int)Channel.Green].keys;
            var blueKeys = rawCurves[(int)Channel.Blue].keys;
            var alphaKeys = rawCurves[(int)Channel.Alpha].keys;
            
            int maxColorCount = Math.Max(Math.Max(redKeys.Length, greenKeys.Length), blueKeys.Length);
            var timeColorDic = new Dictionary<float, (float r, float g, float b, int count)>(maxColorCount);
            
            // R
            for (int i = 0; i < redKeys.Length; i++)
            {
                float time = redKeys[i].time;
                timeColorDic[time] = (redKeys[i].value, 0, 0, 1);
            }
            
            // G
            for (int i = 0; i < greenKeys.Length; i++)
            {
                float time = greenKeys[i].time;
                if (timeColorDic.TryGetValue(time, out var existing))
                {
                    timeColorDic[time] = (existing.r, greenKeys[i].value, existing.b, existing.count + 1);
                }
                else
                {
                    timeColorDic[time] = (0, greenKeys[i].value, 0, 1);
                }
            }
            
            // B
            for (int i = 0; i < blueKeys.Length; i++)
            {
                float time = blueKeys[i].time;
                if (timeColorDic.TryGetValue(time, out var existing))
                {
                    timeColorDic[time] = (existing.r, existing.g, blueKeys[i].value, existing.count + 1);
                }
                else
                {
                    timeColorDic[time] = (0, 0, blueKeys[i].value, 1);
                }
            }

            // Collect aligned RGB channels
            var validColorKeys = new List<GradientColorKey>(timeColorDic.Count);
            foreach (var kvp in timeColorDic)
            {
                if (kvp.Value.count == 3)
                {
                    validColorKeys.Add(new GradientColorKey(
                        new Color(kvp.Value.r, kvp.Value.g, kvp.Value.b),
                        kvp.Key));
                }
            }
            
            validColorKeys.Sort((a, b) => a.time.CompareTo(b.time));
            int colorKeyCount = Math.Min(validColorKeys.Count, maxGradientKeyCount);
            var colorKeys = new GradientColorKey[colorKeyCount];
            for (int i = 0; i < colorKeyCount; i++)
            {
                colorKeys[i] = validColorKeys[i];
            }

            // A
            int alphaKeyCount = Math.Min(alphaKeys.Length, maxGradientKeyCount);
            var alphaKeysArray = new GradientAlphaKey[alphaKeyCount];
            for (int i = 0; i < alphaKeyCount; i++)
            {
                alphaKeysArray[i] = new GradientAlphaKey(alphaKeys[i].value, alphaKeys[i].time);
            }

            return new Gradient
            {
                colorKeys = colorKeys,
                alphaKeys = alphaKeysArray
            };
        }

        #endregion

        #region Gamma <=> Linear

        public LwguiGradient ConvertColorSpaceWithoutCopy(ColorSpace targetColorSpace)
        {
            for (int c = 0; c < (int)Channel.Num; c++)
            {
                if (c != (int)Channel.Alpha)
                {
                    var keys = rawCurves[c].keys;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        if (targetColorSpace == ColorSpace.Gamma)
                            keys[i].value = Mathf.LinearToGammaSpace(keys[i].value);
                        else
                            keys[i].value = Mathf.GammaToLinearSpace(keys[i].value);
                    }

                    rawCurves[c].keys = keys;
                }
            }

            return this;
        }

        public LwguiGradient gamma => new LwguiGradient(this).ConvertColorSpaceWithoutCopy(ColorSpace.Gamma);
        
        public LwguiGradient linear => new LwguiGradient(this).ConvertColorSpaceWithoutCopy(ColorSpace.Linear);

        #endregion
    }
}