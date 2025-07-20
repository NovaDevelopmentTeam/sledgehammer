using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Veldrid;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Interfaces;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Renderables;
using Sledge.Rendering.Viewports;

namespace Sledge.Rendering.Engine
{
    public class Engine : IDisposable
    {
        internal static Engine Instance { get; } = new Engine();
        public static EngineInterface Interface { get; } = new EngineInterface();

        public GraphicsDevice Device { get; }
        public Scene Scene { get; }
        internal RenderContext Context { get; }

        private CancellationTokenSource _token = new();
        private Stopwatch _timer = new();
        private readonly object _lock = new();
        private readonly List<IViewport> _renderTargets = new();
        private readonly Dictionary<PipelineGroup, List<IPipeline>> _pipelines = new();
        private readonly CommandList _commandList;

        private RgbaFloat _clearColourPerspective;
        private RgbaFloat _clearColourOrthographic;

        // Events for viewport lifecycle
        internal event EventHandler<IViewport> ViewportCreated;
        internal event EventHandler<IViewport> ViewportDestroyed;

        private Engine()
        {
            var options = new GraphicsDeviceOptions
            {
                HasMainSwapchain = false,
                ResourceBindingModel = ResourceBindingModel.Improved,
                SwapchainDepthFormat = PixelFormat.R32_Float
            };

            Device = GraphicsDevice.CreateD3D11(options);
            DetectFeatures(Device);
            Scene = new Scene();
            Context = new RenderContext(Device);
            _commandList = Device.ResourceFactory.CreateCommandList();

            SetClearColour(CameraType.Both, RgbaFloat.Black);

#if DEBUG
            Scene.Add(new FpsMonitor());
#endif

            _pipelines.Add(PipelineGroup.Opaque, new List<IPipeline>());
            _pipelines.Add(PipelineGroup.Transparent, new List<IPipeline>());
            _pipelines.Add(PipelineGroup.Overlay, new List<IPipeline>());

            AddPipeline(new WireframePipeline());
            AddPipeline(new TexturedOpaquePipeline());
            AddPipeline(new BillboardOpaquePipeline());
            AddPipeline(new WireframeModelPipeline());
            AddPipeline(new TexturedModelPipeline());

            AddPipeline(new TexturedAlphaPipeline());
            AddPipeline(new TexturedAdditivePipeline());
            AddPipeline(new BillboardAlphaPipeline());

            AddPipeline(new OverlayPipeline());
        }

        private void DetectFeatures(GraphicsDevice device)
        {
            var dev = device.GetType().GetProperty("Device")?.GetValue(device) as SharpDX.Direct3D11.Device;
            var fl = dev?.FeatureLevel ?? FeatureLevel.Level_10_0; // Default to DX10
            if (fl < FeatureLevel.Level_10_0)
            {
                throw new InvalidOperationException($"Sledge requires DirectX 10, but your computer only has version {fl}.");
            }
            Features.FeatureLevel = fl;
        }

        internal void SetClearColour(CameraType type, RgbaFloat colour)
        {
            lock (_lock)
            {
                if (type == CameraType.Both)
                {
                    _clearColourOrthographic = _clearColourPerspective = colour;
                }
                else if (type == CameraType.Orthographic)
                {
                    _clearColourOrthographic = colour;
                }
                else
                {
                    _clearColourPerspective = colour;
                }
            }
        }

        public void AddPipeline(IPipeline pipeline)
        {
            pipeline.Create(Context);
            lock (_lock)
            {
                _pipelines[pipeline.Group].Add(pipeline);
                _pipelines[pipeline.Group].Sort((a, b) => a.Order.CompareTo(b.Order));
            }
        }

        public void Dispose()
        {
            foreach (var pipeline in _pipelines.SelectMany(x => x.Value))
                pipeline.Dispose();

            _pipelines.Clear();

            foreach (var rt in _renderTargets)
                rt.Dispose();

            _renderTargets.Clear();
            Device.Dispose();
            _token.Dispose();
        }

        public async Task StartAsync()
        {
            _timer.Start();
            await Task.Run(() => RenderLoop(_token.Token));
        }

        public void Stop()
        {
            _token.Cancel();
            _timer.Stop();
            _token = new CancellationTokenSource();
        }

        private void RenderLoop(CancellationToken token)
        {
            var lastFrame = _timer.ElapsedMilliseconds;
            while (!token.IsCancellationRequested)
            {
                var frame = _timer.ElapsedMilliseconds;
                var diff = frame - lastFrame;
                if (diff < 16) // ~60 FPS
                {
                    Thread.Sleep(2);
                    continue;
                }
                lastFrame = frame;
                Render(frame);
                Device.WaitForIdle();
            }
        }

        private void Render(long frame)
        {
            lock (_lock)
            {
                Scene.Update(frame);
                var overlays = Scene.GetOverlayRenderables().ToList();

                foreach (var rt in _renderTargets)
                {
                    rt.Update(frame);
                    rt.Overlay.Build(overlays);
                    if (rt.ShouldRender(frame))
                    {
                        Render(rt);
                    }
                }
            }
        }

        private void Render(IViewport renderTarget)
        {
            _commandList.Begin();
            _commandList.SetFramebuffer(renderTarget.Swapchain.Framebuffer);
            _commandList.ClearDepthStencil(1);

            var cc = renderTarget.Camera.Type == CameraType.Perspective
                ? _clearColourPerspective
                : _clearColourOrthographic;
            _commandList.ClearColorTarget(0, cc);

            // Opaque pipelines
            foreach (var opaque in _pipelines[PipelineGroup.Opaque])
            {
                opaque.SetupFrame(Context, renderTarget);
                opaque.Render(Context, renderTarget, _commandList, Scene.GetRenderables(opaque, renderTarget));
            }

            // Transparent pipelines (sorted by distance)
            {
                var cameraLocation = renderTarget.Camera.Location;
                var transparentPipelines = _pipelines[PipelineGroup.Transparent];

                foreach (var transparent in transparentPipelines)
                {
                    transparent.SetupFrame(Context, renderTarget);
                }

                var locationObjects =
                    from t in transparentPipelines
                    from renderable in Scene.GetRenderables(t, renderTarget)
                    from location in renderable.GetLocationObjects(t, renderTarget)
                    orderby (cameraLocation - location.Location).LengthSquared() descending
                    select new { Pipeline = t, Renderable = renderable, Location = location };

                foreach (var lo in locationObjects)
                {
                    lo.Pipeline.Render(Context, renderTarget, _commandList, lo.Renderable, lo.Location);
                }
            }

            // Overlay pipelines
            foreach (var overlay in _pipelines[PipelineGroup.Overlay])
            {
                overlay.SetupFrame(Context, renderTarget);
                overlay.Render(Context, renderTarget, _commandList, Scene.GetRenderables(overlay, renderTarget));
            }

            _commandList.End();
            Device.SubmitCommands(_commandList);
            Device.SwapBuffers(renderTarget.Swapchain);
        }

        // Viewport management
        internal IViewport CreateViewport()
        {
            lock (_lock)
            {
                var control = new Viewports.Viewport(Device, new GraphicsDeviceOptions
                {
                    HasMainSwapchain = false,
                    ResourceBindingModel = ResourceBindingModel.Improved,
                    SwapchainDepthFormat = PixelFormat.R32_Float
                });
                control.Disposed += DestroyViewport;

                if (!_renderTargets.Any()) _ = StartAsync();
                _renderTargets.Add(control);

                Scene.Add((IRenderable)control.Overlay);
                Scene.Add((IUpdateable)control.Overlay);
                ViewportCreated?.Invoke(this, control);

                return control;
            }
        }

        private void DestroyViewport(object viewport, EventArgs e)
        {
            if (viewport is not IViewport t) return;

            lock (_lock)
            {
                _renderTargets.Remove(t);
                Device.WaitForIdle();

                if (!_renderTargets.Any()) Stop();

                ViewportDestroyed?.Invoke(this, t);
                Scene.Remove((IRenderable)t.Overlay);
                Scene.Remove((IUpdateable)t.Overlay);

                t.Control.Disposed -= DestroyViewport;
                t.Dispose();
            }
        }
    }
}
