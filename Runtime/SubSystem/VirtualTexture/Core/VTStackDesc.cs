using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace VividRP.Runtime
{
    public enum VTLayerSemantic
    {
        BaseColor = 0,
        Normal = 1,
        ORM = 2,
        Height = 3,
        Mask = 4,
    }

    public readonly struct VTLayerDesc : IEquatable<VTLayerDesc>
    {
        public VTLayerDesc(
            VTLayerSemantic semantic,
            GraphicsFormat graphicsFormat,
            bool sRGB,
            Color32 fallbackColor,
            int physicalGroup = 0,
            VTLayerDataEncoding encoding = VTLayerDataEncoding.RGBA)
        {
            if (graphicsFormat == GraphicsFormat.None)
                throw new ArgumentException("Layer graphics format must be valid.", nameof(graphicsFormat));
            if (GraphicsFormatUtility.IsCompressedFormat(graphicsFormat)
                && graphicsFormat != GraphicsFormat.R_BC4_UNorm
                && graphicsFormat != GraphicsFormat.RG_BC5_UNorm
                && graphicsFormat != GraphicsFormat.RGBA_BC7_UNorm
                && graphicsFormat != GraphicsFormat.RGBA_BC7_SRGB)
            {
                throw new ArgumentException(
                    "Only desktop BC4, BC5 and BC7 compressed virtual texture cache formats are supported.",
                    nameof(graphicsFormat));
            }
            if (GraphicsFormatUtility.IsDepthFormat(graphicsFormat)
                || GraphicsFormatUtility.IsStencilFormat(graphicsFormat))
                throw new ArgumentException("Virtual texture layers must use a color graphics format.", nameof(graphicsFormat));
            if (physicalGroup < 0)
                throw new ArgumentOutOfRangeException(nameof(physicalGroup));

            Semantic = semantic;
            GraphicsFormat = graphicsFormat;
            SRGB = sRGB;
            FallbackColor = fallbackColor;
            PhysicalGroup = physicalGroup;
            Encoding = encoding;
        }

        public VTLayerSemantic Semantic { get; }

        public GraphicsFormat GraphicsFormat { get; }

        public bool SRGB { get; }

        public Color32 FallbackColor { get; }

        public int PhysicalGroup { get; }

        public VTLayerDataEncoding Encoding { get; }

        public bool Equals(VTLayerDesc other)
        {
            return Semantic == other.Semantic
                   && GraphicsFormat == other.GraphicsFormat
                   && SRGB == other.SRGB
                   && FallbackColor.Equals(other.FallbackColor)
                   && PhysicalGroup == other.PhysicalGroup
                   && Encoding == other.Encoding;
        }

        public override bool Equals(object obj)
        {
            return obj is VTLayerDesc other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Semantic, GraphicsFormat, SRGB, FallbackColor, PhysicalGroup, Encoding);
        }
    }

    public readonly struct VTStackDesc : IEquatable<VTStackDesc>
    {
        public const int MaxLayerCount = 4;

        public VTStackDesc(
            int pageSize,
            int borderSize,
            int cachePageCount,
            GraphicsFormat graphicsFormat,
            int maxUploadsPerFrame,
            int feedbackCapacity,
            int neighborPrefetchCount = 0)
            : this(
                pageSize,
                borderSize,
                cachePageCount,
                new[]
                {
                    new VTLayerDesc(
                        VTLayerSemantic.BaseColor,
                        graphicsFormat,
                        GraphicsFormatUtility.IsSRGBFormat(graphicsFormat),
                        new Color32(0, 0, 0, 255)),
                },
                maxUploadsPerFrame,
                feedbackCapacity,
                neighborPrefetchCount)
        {
        }

        public VTStackDesc(
            int pageSize,
            int borderSize,
            int cachePageCount,
            VTLayerDesc[] layers,
            int maxUploadsPerFrame,
            int feedbackCapacity,
            int neighborPrefetchCount = 0)
        {
            if (pageSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (borderSize < 0)
                throw new ArgumentOutOfRangeException(nameof(borderSize));
            if (cachePageCount <= 0 || cachePageCount > VirtualTexturePageTableEntry.MaxPhysicalPageCount)
                throw new ArgumentOutOfRangeException(nameof(cachePageCount));
            if (layers == null || layers.Length == 0)
                throw new ArgumentException("Virtual texture stack must contain at least one layer.", nameof(layers));
            if (layers.Length > MaxLayerCount)
                throw new ArgumentOutOfRangeException(nameof(layers), $"Virtual texture stack supports at most {MaxLayerCount} layers.");
            if (maxUploadsPerFrame <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxUploadsPerFrame));
            if (feedbackCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(feedbackCapacity));

            PageSize = pageSize;
            BorderSize = borderSize;
            CachePageCount = cachePageCount;
            m_Layers = new VTLayerDesc[layers.Length];
            var groupFormats = new Dictionary<int, GraphicsFormat>();
            for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                if (layers[layerIndex].GraphicsFormat == GraphicsFormat.None)
                    throw new ArgumentException("Layer graphics format must be valid.", nameof(layers));
                if (layers[layerIndex].PhysicalGroup >= MaxLayerCount)
                    throw new ArgumentOutOfRangeException(
                        nameof(layers),
                        $"Physical group index must be smaller than {MaxLayerCount}.");

                GraphicsFormat storageFormat = GraphicsFormatUtility.GetLinearFormat(layers[layerIndex].GraphicsFormat);
                if (groupFormats.TryGetValue(layers[layerIndex].PhysicalGroup, out GraphicsFormat groupFormat)
                    && groupFormat != storageFormat)
                {
                    throw new ArgumentException(
                        $"Virtual texture physical group {layers[layerIndex].PhysicalGroup} mixes storage formats " +
                        $"{groupFormat} and {storageFormat}.",
                        nameof(layers));
                }

                groupFormats[layers[layerIndex].PhysicalGroup] = storageFormat;

                m_Layers[layerIndex] = layers[layerIndex];
            }

            GraphicsFormat = m_Layers[0].GraphicsFormat;
            MaxUploadsPerFrame = maxUploadsPerFrame;
            FeedbackCapacity = feedbackCapacity;
            NeighborPrefetchCount = Mathf.Clamp(neighborPrefetchCount, 0, 4);
        }

        private readonly VTLayerDesc[] m_Layers;

        public int PageSize { get; }

        public int BorderSize { get; }

        public int CachePageCount { get; }

        public int LayerCount => m_Layers?.Length ?? 0;

        public IReadOnlyList<VTLayerDesc> Layers => m_Layers ?? Array.Empty<VTLayerDesc>();

        public GraphicsFormat GraphicsFormat { get; }

        public bool SRGB => LayerCount > 0 && m_Layers[0].SRGB;

        public Color32 FallbackColor => LayerCount > 0 ? m_Layers[0].FallbackColor : new Color32(0, 0, 0, 255);

        public int MaxUploadsPerFrame { get; }

        public int FeedbackCapacity { get; }

        public int NeighborPrefetchCount { get; }

        public int PhysicalPageSize => PageSize + BorderSize * 2;

        public VTLayerDesc GetLayer(int layerIndex)
        {
            if (m_Layers == null || layerIndex < 0 || layerIndex >= m_Layers.Length)
                throw new ArgumentOutOfRangeException(nameof(layerIndex));

            return m_Layers[layerIndex];
        }

        public bool TryGetLayerIndex(VTLayerSemantic semantic, out int layerIndex)
        {
            if (m_Layers != null)
            {
                for (int index = 0; index < m_Layers.Length; index++)
                {
                    if (m_Layers[index].Semantic != semantic)
                        continue;

                    layerIndex = index;
                    return true;
                }
            }

            layerIndex = -1;
            return false;
        }

        public int GetLayerIndexOrDefault(VTLayerSemantic semantic, int defaultLayerIndex = 0)
        {
            return TryGetLayerIndex(semantic, out int layerIndex) ? layerIndex : defaultLayerIndex;
        }

        public bool Equals(VTStackDesc other)
        {
            if (PageSize != other.PageSize
                || BorderSize != other.BorderSize
                || CachePageCount != other.CachePageCount
                || MaxUploadsPerFrame != other.MaxUploadsPerFrame
                || FeedbackCapacity != other.FeedbackCapacity
                || NeighborPrefetchCount != other.NeighborPrefetchCount
                || LayerCount != other.LayerCount)
            {
                return false;
            }

            for (int layerIndex = 0; layerIndex < LayerCount; layerIndex++)
            {
                if (!m_Layers[layerIndex].Equals(other.m_Layers[layerIndex]))
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is VTStackDesc other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(PageSize);
            hashCode.Add(BorderSize);
            hashCode.Add(CachePageCount);
            hashCode.Add(MaxUploadsPerFrame);
            hashCode.Add(FeedbackCapacity);
            hashCode.Add(NeighborPrefetchCount);
            for (int layerIndex = 0; layerIndex < LayerCount; layerIndex++)
                hashCode.Add(m_Layers[layerIndex]);

            return hashCode.ToHashCode();
        }
    }
}
