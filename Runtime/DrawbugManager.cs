﻿using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering.Universal;

namespace Drawbug
{
    internal class DrawbugManager : MonoBehaviour
    {
        internal enum RenderPipelineOption
        {
            BuiltIn,
            Custom,
            URP,
            HDRP
        }
        
        private static DrawbugManager _instance;

        private Draw _draw;
        private CommandBuffer _cmd;
        
        private bool _isEnabled;
        private RenderPipelineOption _currentRenderPipeline = RenderPipelineOption.BuiltIn;
#if PACKAGE_UNIVERSAL_RP
        private DrawbugRenderPassFeature _renderPassFeature;
#endif
        
        public static void Initialize()
        {
            if(_instance)
                return;

            var gameObj = new GameObject(string.Concat("DrawbugManager (", Random.Range(0, 10000).ToString("0000"), ")"))
            {
                //hideFlags = HideFlags.DontSave | HideFlags.NotEditable | HideFlags.HideInHierarchy | HideFlags.HideInInspector
            };
            Debug.Log(gameObj.name + " Initilized");
            _instance = gameObj.AddComponent<DrawbugManager>();
            // if (Application.isPlaying)
            // {
            //     print("Dont Destroy");
            //     DontDestroyOnLoad(gameObj);
            // }
        }

        private void UpdateCurrentRenderPipeline()
        {
            var pipelineType = RenderPipelineManager.currentPipeline != null ? RenderPipelineManager.currentPipeline.GetType() : null;
            
#if PACKAGE_HIGH_DEFINITION_RP
            if (pipelineType == typeof(HDRenderPipeline)) {
                if (_currentRenderPipeline != RenderPipelineOption.HDRP) {
                    _currentRenderPipeline = RenderPipelineOption.HDRP;
                    if (!_instance.gameObject.TryGetComponent(out CustomPassVolume volume)) {
                        volume = _instance.gameObject.AddComponent<CustomPassVolume>();
                        volume.isGlobal = true;
                        volume.injectionPoint = CustomPassInjectionPoint.AfterPostProcess;
                        volume.customPasses.Add(new DrawbugHDRPCustomPass());
                    }

                    var asset = GraphicsSettings.defaultRenderPipeline as HDRenderPipelineAsset;
                    if (asset != null) {
                        if (!asset.currentPlatformRenderPipelineSettings.supportCustomPass) {
                            Debug.LogWarning("DebugTool: Custom pass support is disabled in the current render pipeline. Please enable it in the HDRenderPipelineAsset.", asset);
                        }
                    }
                }
                return;
            }
#endif
#if PACKAGE_UNIVERSAL_RP
            if (pipelineType == typeof(UniversalRenderPipeline)) {
                _currentRenderPipeline = RenderPipelineOption.URP;
                return;
            }
#endif
            _currentRenderPipeline = pipelineType != null ? RenderPipelineOption.Custom : RenderPipelineOption.BuiltIn;
        }

        private void OnEnable()
        {
            if (_instance == null)
                _instance = this;

            if (_instance != this)
            {
                DestroyImmediate(gameObject);
                return;
            }
            
            _isEnabled = true;
            _draw = new Draw();
            InsertToPlayerLoop();
            _cmd = new CommandBuffer()
            {
                name = "Drawbug"
            };
            
            // Configura callback para renderização com pipeline padrão
            Camera.onPostRender += PostRender;
            // Configura callback para renderização com pipeline scriptavel
#if UNITY_2023_3_OR_NEWER
            RenderPipelineManager.beginContextRendering += BeginContextRendering;
#else
			RenderPipelineManager.beginFrameRendering += BeginFrameRendering;
#endif
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
        }

        private void OnDisable()
        {
            if (!_isEnabled)
                return;

            _isEnabled = false;
            _instance = null;
            RemoveFromPlayerLoop();
            _draw.Dispose();
            _cmd.Dispose();
            
            Camera.onPostRender -= PostRender;
#if UNITY_2023_3_OR_NEWER
            RenderPipelineManager.beginContextRendering -= BeginContextRendering;
#else
			RenderPipelineManager.beginFrameRendering -= BeginFrameRendering;
#endif
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            
#if PACKAGE_UNIVERSAL_RP
			if (_renderPassFeature != null) {
				DestroyImmediate(_renderPassFeature);
				_renderPassFeature = null;
			}
#endif
        }
        
        void BeginContextRendering (ScriptableRenderContext context, List<Camera> cameras) 
        {
            UpdateCurrentRenderPipeline();
        }

        void BeginFrameRendering (ScriptableRenderContext context, Camera[] cameras) 
        {
            UpdateCurrentRenderPipeline();
        }

        void BeginCameraRendering (ScriptableRenderContext context, Camera camera) 
        {
#if PACKAGE_UNIVERSAL_RP
			if (_currentRenderPipeline == RenderPipelineOption.URP) 
            {
				var data = camera.GetUniversalAdditionalCameraData();
				if (data != null) {
					var renderer = data.scriptableRenderer;
					if (_renderPassFeature == null) {
						_renderPassFeature = ScriptableObject.CreateInstance<DrawbugRenderPassFeature>();
					}
					_renderPassFeature.AddRenderPasses(renderer);
				}
			}
#endif
        }
        
        private void EndCameraRendering (ScriptableRenderContext context, Camera camera) {
            if (_currentRenderPipeline == RenderPipelineOption.Custom) 
            {
                ExecuteCustomRenderPass(context, camera);
            }
        }
        
        void PostRender (Camera camera) 
        {
            if (_hasPendingData)
            {
                _hasPendingData = false;
                _draw.GetDataResults();
            }
            
            _cmd.Clear();
            _draw.Render(_cmd);
            Graphics.ExecuteCommandBuffer(_cmd);
        }
        
        private struct BuildDrawbugCommands
        {
            
        }
        private struct ClearDrawbug
        {
            
        }

        private void InsertToPlayerLoop()
        {
            PlayerLoopInserter.InsertSystem(typeof(BuildDrawbugCommands), typeof(UnityEngine.PlayerLoop.PostLateUpdate), InsertType.Before, BuildCommandsUpdate);
            PlayerLoopInserter.InsertSystem(typeof(ClearDrawbug), typeof(UnityEngine.PlayerLoop.EarlyUpdate), InsertType.Before, ClearFrameData);
        }

        private void ClearFrameData()
        {
            _draw.Clear();
        }

        private void RemoveFromPlayerLoop()
        {
            PlayerLoopInserter.RemoveRunner(typeof(BuildDrawbugCommands));
            PlayerLoopInserter.RemoveRunner(typeof(ClearDrawbug));
        }

        private bool _hasPendingData;
        
        private void BuildCommandsUpdate()
        {
            if (_hasPendingData)
                return;
         
            _draw.BuildData();
            _hasPendingData = true;
        }

        private void RenderCustomPass(CommandBuffer cmd, Camera camera)
        {
            if (_hasPendingData)
            {
                _hasPendingData = false;
                _draw.GetDataResults();
            }
            
            _draw.Render(cmd);
        }
        
        internal static void ExecuteCustomRenderPass(ScriptableRenderContext context, Camera camera)
        {
            if(!_instance._isEnabled)
                return;
            
            _instance._cmd.Clear();
            _instance.RenderCustomPass(_instance._cmd, camera);
            context.ExecuteCommandBuffer(_instance._cmd);
        }
        
#if PACKAGE_UNIVERSAL_RP
        private void RenderGraphPass(RasterCommandBuffer cmd, Camera camera)
        {
            if (_hasPendingData)
            {
                _hasPendingData = false;
                _draw.GetDataResults();
            }
            
            _draw.Render(cmd);
        }

        internal static void ExecuteCustomRenderGraphPass(RasterCommandBuffer cmd, Camera camera)
        {
            if(!_instance || !_instance._isEnabled)
                return;
            
            _instance.RenderGraphPass(cmd, camera);
        }
#endif

#if PACKAGE_HIGH_DEFINITION_RP
        internal static void ExecuteCustomPass(CommandBuffer cmd, Camera camera)
        {
            if(!_instance._isEnabled)
                return;
            
            _instance.RenderCustomPass(cmd, camera);
        }
#endif
    }
}
