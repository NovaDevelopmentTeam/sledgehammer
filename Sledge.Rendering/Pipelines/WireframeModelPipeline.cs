using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Numerics;
using Sledge.Rendering.Engine;
using Sledge.Rendering.Interfaces;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Renderables;
using Sledge.Rendering.Viewports;
using Veldrid;

namespace Sledge.Rendering.Pipelines
{
    public class WireframeModelPipeline : IPipeline, IDisposable
    {
        public PipelineType Type => PipelineType.WireframeModel;
        public PipelineGroup Group => PipelineGroup.Opaque;
        public float Order => 7;

        private Shader _vertex;
        private Shader _fragment;
        private Pipeline _pipeline;
        private DeviceBuffer _projectionBuffer;
        private ResourceSet _projectionResourceSet;
        private ResourceLayout _transformsLayout;
        private UniformProjection _lastProjection;

        public void Create(RenderContext context)
        {
            (_vertex, _fragment) = context.ResourceLoader.LoadShaders(Type.ToString());

            _transformsLayout = context.Device.ResourceFactory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("uTransforms", ResourceKind.UniformBuffer, ShaderStages.Vertex)
                )
            );

            var pDesc = new GraphicsPipelineDescription
            {
                BlendState = BlendStateDescription.SingleDisabled,
                DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
                RasterizerState = RasterizerStateDescription.Default,
                PrimitiveTopology = PrimitiveTopology.LineList,
                ResourceLayouts = new[] { context.ResourceLoader.ProjectionLayout, _transformsLayout },
                ShaderSet = new ShaderSetDescription(
                    new[] { context.ResourceLoader.VertexModel3LayoutDescription },
                    new[] { _vertex, _fragment }
                ),
                Outputs = new OutputDescription
                {
                    ColorAttachments = new[] { new OutputAttachmentDescription(PixelFormat.B8_G8_R8_A8_UNorm) },
                    DepthAttachment = new OutputAttachmentDescription(PixelFormat.R32_Float),
                    SampleCount = TextureSampleCount.Count1
                }
            };

            _pipeline = context.Device.ResourceFactory.CreateGraphicsPipeline(ref pDesc);

            _projectionBuffer = context.Device.ResourceFactory.CreateBuffer(
                new BufferDescription((uint)Unsafe.SizeOf<UniformProjection>(), BufferUsage.UniformBuffer)
            );

            _projectionResourceSet = context.Device.ResourceFactory.CreateResourceSet(
                new ResourceSetDescription(context.ResourceLoader.ProjectionLayout, _projectionBuffer)
            );
        }

        public void SetupFrame(RenderContext context, IViewport target)
        {
            // Optional: Buffer einmal pro Frame aktualisieren, falls Model-Transformation identisch bleibt
            // var projection = new UniformProjection
            // {
            //     Selective = context.SelectiveTransform,
            //     Model = Matrix4x4.Identity,
            //     View = target.Camera.View,
            //     Projection = target.Camera.Projection,
            // };
            // cl.UpdateBuffer(_projectionBuffer, 0, projection);
            // _lastProjection = projection;
        }

        public void Render(RenderContext context, IViewport target, CommandList cl, IEnumerable<IRenderable> renderables)
        {
            cl.SetPipeline(_pipeline);
            cl.SetGraphicsResourceSet(0, _projectionResourceSet);

            foreach (var r in renderables)
            {
                if (r is IModelRenderable modelRenderable)
                {
                    var projection = new UniformProjection
                    {
                        Selective = context.SelectiveTransform,
                        Model = modelRenderable.GetModelTransformation(),
                        View = target.Camera.View,
                        Projection = target.Camera.Projection,
                    };

                    if (!projection.Equals(_lastProjection))
                    {
                        cl.UpdateBuffer(_projectionBuffer, 0, projection);
                        _lastProjection = projection;
                    }

                    modelRenderable.Render(context, this, target, cl);
                }
            }
        }

        public void Render(RenderContext context, IViewport target, CommandList cl, IRenderable renderable, ILocation locationObject)
        {
            cl.SetPipeline(_pipeline);
            cl.SetGraphicsResourceSet(0, _projectionResourceSet);

            if (renderable is IModelRenderable modelRenderable)
            {
                var projection = new UniformProjection
                {
                    Selective = context.SelectiveTransform,
                    Model = modelRenderable.GetModelTransformation(),
                    View = target.Camera.View,
                    Projection = target.Camera.Projection,
                };

                if (!projection.Equals(_lastProjection))
                {
                    cl.UpdateBuffer(_projectionBuffer, 0, projection);
                    _lastProjection = projection;
                }
            }

            renderable.Render(context, this, target, cl, locationObject);
        }

        public void Bind(RenderContext context, CommandList cl, string binding)
        {
            var tex = context.ResourceLoader.GetTexture(binding);
            tex?.BindTo(cl, 1);
        }

        public void Dispose()
        {
            _projectionResourceSet?.Dispose(); _projectionResourceSet = null;
            _projectionBuffer?.Dispose(); _projectionBuffer = null;
            _pipeline?.Dispose(); _pipeline = null;
            _transformsLayout?.Dispose(); _transformsLayout = null;
            _vertex?.Dispose(); _vertex = null;
            _fragment?.Dispose(); _fragment = null;
        }
    }
}
