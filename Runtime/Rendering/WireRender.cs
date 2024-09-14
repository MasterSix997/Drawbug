﻿using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;

namespace Drawbug
{
    internal class WireRender : IDisposable
    {
        private readonly Material _material = new(Resources.Load<Shader>("WireShader"));

        private int _positionsCount;

        private static readonly int PositionsProperty = Shader.PropertyToID("_Positions");
        private static readonly int StyleDataProperty = Shader.PropertyToID("_StyleData");
        
        private GraphicsBuffer _positions;
        private GraphicsBuffer _styleData;

        internal unsafe void UpdateBuffer(WireBuffer positions, NativeArray<DrawCommandBuffer.StyleData> styleData, int count)
        {
            Profiler.BeginSample("Update Buffer");
            if (_positions == null || _positions.count < count)
            {
                _positions?.Dispose();
                _positions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, 
                    count, sizeof(PositionData));
                
                _styleData?.Dispose();
                _styleData = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 
                    count, sizeof(DrawCommandBuffer.StyleData));
            }
            
            _positionsCount = count;
            
            positions.FillBuffer(_positions);
            _styleData.SetData(styleData);
            
            _material.SetBuffer(PositionsProperty, _positions);
            _material.SetBuffer(StyleDataProperty, _styleData);
            Profiler.EndSample();
        }

        internal void Render(UnityEngine.Rendering.CommandBuffer cmd)
        {
            cmd.DrawProcedural(Matrix4x4.identity, _material, -1, MeshTopology.Lines, _positionsCount);
        }
        
        internal void Render(UnityEngine.Rendering.RasterCommandBuffer cmd)
        {
            cmd.DrawProcedural(Matrix4x4.identity, _material, -1, MeshTopology.Lines, _positionsCount);
        }

        public void Dispose()
        {
            _positions?.Dispose();
            _styleData?.Dispose();
        }
    }
}