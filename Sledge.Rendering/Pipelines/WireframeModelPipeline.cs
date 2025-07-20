using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
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
        private DeviceBuffer _transformsBuffer;
        private ResourceSet _transformsResourceSet;

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

            // NEW: Bone transforms buffer & resource set (128 matrices)
            _transformsBuffer = context.Device.ResourceFactory.CreateBuffer(
                new BufferDescription((uint)(Unsafe.SizeOf<Matrix4x4>() * 128), BufferUsage.UniformBuffer)
            );
            _transformsResourceSet = context.Device.ResourceFactory.CreateResourceSet(
                new ResourceSetDescription(_transformsLayout, _transformsBuffer)
            );
        }

        public void SetupFrame(RenderContext context, IViewport target)
        {
            // You could update global transforms here, if needed
        }

        public void Render(RenderContext context, IViewport target, CommandList cl, IEnumerable<IRenderable> renderables)
        {
            cl.SetPipeline(_pipeline);
            cl.SetGraphicsResourceSet(0, _projectionResourceSet);
            cl.SetGraphicsResourceSet(1, _transformsResourceSet);

            foreach (var r in renderables.OfType<IModelRenderable>())
            {
                cl.UpdateBuffer(_projectionBuffer, 0, new UniformProjection
                {
                    Selective = context.SelectiveTransform,
                    Model = r.GetModelTransformation(),
                    View = target.Camera.View,
                    Projection = target.Camera.Projection,
                });

                // Update bone transforms if available
                Matrix4x4[] boneMatrices = r.GetBoneMatrices(); // Should return 128 or less
                if (boneMatrices != null && boneMatrices.Length > 0)
                {
                    // Pad to 128 if needed
                    Matrix4x4[] padded = new Matrix4x4[128];
                    boneMatrices.CopyTo(padded, 0);
                    cl.UpdateBuffer(_transformsBuffer, 0, padded);
                }
                else
                {
                    // Zero out if not used
                    cl.UpdateBuffer(_transformsBuffer, 0, new Matrix4x4[128]);
                }

                r.Render(context, this, target, cl);
            }
        }

        public void Render(RenderContext context, IViewport target, CommandList cl, IRenderable renderable, ILocation locationObject)
        {
            cl.SetPipeline(_pipeline);
            cl.SetGraphicsResourceSet(0, _projectionResourceSet);
            cl.SetGraphicsResourceSet(1, _transformsResourceSet);

            if (renderable is IModelRenderable r)
            {
                cl.UpdateBuffer(_projectionBuffer, 0, new UniformProjection
                {
                    Selective = context.SelectiveTransform,
                    Model = r.GetModelTransformation(),
                    View = target.Camera.View,
                    Projection = target.Camera.Projection,
                });

                Matrix4x4[] boneMatrices = r.GetBoneMatrices();
                if (boneMatrices != null && boneMatrices.Length > 0)
                {
                    Matrix4x4[] padded = new Matrix4x4[128];
                    boneMatrices.CopyTo(padded, 0);
                    cl.UpdateBuffer(_transformsBuffer, 0, padded);
                }
                else
                {
                    cl.UpdateBuffer(_transformsBuffer, 0, new Matrix4x4[128]);
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
            _transformsResourceSet?.Dispose(); _transformsResourceSet = null;
            _transformsBuffer?.Dispose(); _transformsBuffer = null;
            _pipeline?.Dispose(); _pipeline = null;
            _transformsLayout?.Dispose(); _transformsLayout = null;
            _vertex?.Dispose(); _vertex = null;
            _fragment?.Dispose(); _fragment = null;
        }
    }
}
