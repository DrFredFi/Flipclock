using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Flipclock.Core.Clock;
using Flipclock.Core.Settings;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Flipclock.Rendering;

/// <summary>
/// Draws the clock with Direct3D 11 into a composition swap chain. World units are pixels:
/// the z = 0 plane maps 1:1 onto the window, tiles stand in front of it and the background sits behind.
/// </summary>
public sealed class ClockRenderer : IDisposable
{
    // Tile proportions in units of tile width.
    const float TileHeight = (float)(1 / ClockLayout.TileAspect);
    const float SplitGap = 0.016f;
    const float SlabDepth = 0.11f;
    const float FlapDepth = 0.016f;
    const float CornerRadius = 0.075f;
    const float Bevel = 0.02f;
    const float AxleRadius = 0.02f;
    const float BackgroundDepth = 0.32f; // in tile heights
    const float FieldOfView = 20f * MathF.PI / 180f;
    const int ShadowMapSize = 2048;
    const int SampleCount = 4;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct FrameConstants
    {
        public Matrix4x4 ViewProj;
        public Matrix4x4 LightViewProj;
        public Vector4 LightDir;
        public Vector4 LightColor;
        public Vector4 CameraPos;
        public Vector4 DigitColor;
        public Vector4 BgColor;
        public Vector4 BgUv;
        public Vector4 Params;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct ObjectConstants
    {
        public Matrix4x4 World;
        public Matrix4x4 NormalMatrix;
        public Vector4 BaseColor;
        public Vector4 Material;
    }

    sealed class GpuMesh(ID3D11Buffer vertices, ID3D11Buffer indices, uint indexCount) : IDisposable
    {
        public ID3D11Buffer Vertices { get; } = vertices;
        public ID3D11Buffer Indices { get; } = indices;
        public uint IndexCount { get; } = indexCount;
        public void Dispose() { Vertices.Dispose(); Indices.Dispose(); }
    }

    readonly record struct DrawItem(GpuMesh Mesh, Matrix4x4 World, Vector4 Color, Vector4 Material);

    readonly ID3D11Device _device;
    readonly ID3D11DeviceContext _context;
    readonly IDXGISwapChain1 _swapChain;

    readonly ID3D11VertexShader _vs, _vsShadow;
    readonly ID3D11PixelShader _ps, _psBackground;
    readonly ID3D11InputLayout _layout;
    readonly ID3D11Buffer _frameCb, _objectCb;
    readonly ID3D11SamplerState _linearSampler, _shadowSampler;
    readonly ID3D11RasterizerState _rasterizer, _shadowRasterizer;
    readonly ID3D11DepthStencilState _depthState;

    readonly GpuMesh _topHalf, _bottomHalf, _flap, _sphere, _axle, _quad;

    readonly ID3D11Texture2D _shadowMap;
    readonly ID3D11DepthStencilView _shadowDsv;
    readonly ID3D11ShaderResourceView _shadowSrv;

    ID3D11Texture2D? _glyphs;
    ID3D11ShaderResourceView? _glyphSrv;
    ID3D11Texture2D? _bgImage;
    ID3D11ShaderResourceView? _bgSrv;
    int _bgWidth, _bgHeight;

    ID3D11Texture2D? _msaaColor, _msaaDepth;
    ID3D11RenderTargetView? _msaaRtv;
    ID3D11DepthStencilView? _msaaDsv;
    int _width = 1, _height = 1;

    readonly List<DrawItem> _draws = new(64);

    public ClockRenderer(int width, int height)
    {
        (_device, _context, IsSoftware) = CreateDevice();

        using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        var desc = new SwapChainDescription1
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Format = Format.B8G8R8A8_UNorm,
            BufferCount = 2,
            BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0),
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = AlphaMode.Ignore,
        };
        _swapChain = factory.CreateSwapChainForComposition(_device, desc);

        var source = LoadShaderSource();
        var vsCode = Compile(source, "VSMain", "vs_5_0");
        _vs = _device.CreateVertexShader(vsCode);
        _vsShadow = _device.CreateVertexShader(Compile(source, "VSShadow", "vs_5_0"));
        _ps = _device.CreatePixelShader(Compile(source, "PSMain", "ps_5_0"));
        _psBackground = _device.CreatePixelShader(Compile(source, "PSBackground", "ps_5_0"));
        _layout = _device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 24, 0),
            new InputElementDescription("TEXCOORD", 1, Format.R32_Float, 32, 0),
        ], vsCode);

        _frameCb = _device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer));
        _objectCb = _device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<ObjectConstants>(), BindFlags.ConstantBuffer));

        _linearSampler = _device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            MaxLOD = float.MaxValue,
            MaxAnisotropy = 1,
            ComparisonFunc = ComparisonFunction.Never,
        });
        _shadowSampler = _device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.ComparisonMinMagLinearMipPoint,
            AddressU = TextureAddressMode.Border,
            AddressV = TextureAddressMode.Border,
            AddressW = TextureAddressMode.Border,
            BorderColor = new Color4(1, 1, 1, 1),
            ComparisonFunc = ComparisonFunction.LessEqual,
            MaxLOD = float.MaxValue,
            MaxAnisotropy = 1,
        });

        _rasterizer = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid)
        {
            MultisampleEnable = true,
            AntialiasedLineEnable = false,
            DepthClipEnable = true,
        });
        _shadowRasterizer = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid)
        {
            DepthBias = 800,
            SlopeScaledDepthBias = 2.0f,
            DepthBiasClamp = 0.01f,
            DepthClipEnable = true,
        });
        _depthState = _device.CreateDepthStencilState(DepthStencilDescription.Default);

        _topHalf = Upload(MeshData.HalfTile(TileHeight, SplitGap, 0, SlabDepth, CornerRadius, Bevel, top: true));
        _bottomHalf = Upload(MeshData.HalfTile(TileHeight, SplitGap, 0, SlabDepth, CornerRadius, Bevel, top: false));
        _flap = Upload(MeshData.HalfTile(TileHeight, SplitGap, SlabDepth, SlabDepth + FlapDepth, CornerRadius, Bevel, top: true));
        _sphere = Upload(MeshData.Sphere());
        _axle = Upload(MeshData.CylinderX());
        _quad = Upload(MeshData.Quad());

        _shadowMap = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = ShadowMapSize,
            Height = ShadowMapSize,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R32_Typeless,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource,
        });
        _shadowDsv = _device.CreateDepthStencilView(_shadowMap, new DepthStencilViewDescription(DepthStencilViewDimension.Texture2D, Format.D32_Float));
        _shadowSrv = _device.CreateShaderResourceView(_shadowMap, new ShaderResourceViewDescription(ShaderResourceViewDimension.Texture2D, Format.R32_Float, 0, 1));

        CreateTargets();
        SetGlyphFont("Segoe UI", 700);
    }

    /// <summary>The native IDXGISwapChain1 pointer, for attaching to a SwapChainPanel.</summary>
    public nint SwapChainPointer => _swapChain.NativePointer;

    /// <summary>True when running on the WARP software rasterizer.</summary>
    public bool IsSoftware { get; }

    static (ID3D11Device, ID3D11DeviceContext, bool) CreateDevice()
    {
        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];
        var flags = DeviceCreationFlags.BgraSupport;
        if (D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, levels, out ID3D11Device? device, out _, out ID3D11DeviceContext? context).Success
            && device is not null && context is not null)
            return (device, context, false);

        D3D11.D3D11CreateDevice(null, DriverType.Warp, flags, levels, out device, out _, out context).CheckError();
        return (device!, context!, true);
    }

    static string LoadShaderSource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Flipclock.Rendering.Clock.hlsl")
                           ?? throw new InvalidOperationException("Shader resource missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static byte[] Compile(string source, string entry, string profile)
    {
        var result = Compiler.Compile(source, null!, null!, entry, "Clock.hlsl", profile,
            ShaderFlags.OptimizationLevel3, EffectFlags.None, out Blob? blob, out Blob? errors);
        if (result.Failure || blob is null)
            throw new InvalidOperationException($"Shader {entry} failed to compile: {errors?.AsString()}");
        var bytes = blob.AsBytes();
        blob.Dispose();
        errors?.Dispose();
        return bytes;
    }

    GpuMesh Upload(MeshData mesh)
    {
        var vb = _device.CreateBuffer(CollectionsMarshal.AsSpan(mesh.Vertices), BindFlags.VertexBuffer);
        var ib = _device.CreateBuffer(CollectionsMarshal.AsSpan(mesh.Indices), BindFlags.IndexBuffer);
        return new GpuMesh(vb, ib, (uint)mesh.Indices.Count);
    }

    public void Resize(int width, int height, float scaleX, float scaleY)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width != _width || height != _height)
        {
            _context.OMSetRenderTargets((ID3D11RenderTargetView?)null, null);
            ReleaseTargets();
            _context.Flush();
            _swapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
            _width = width;
            _height = height;
            CreateTargets();
        }
        // The panel scales its content by the DPI factor; undo it so one buffer pixel is one screen pixel.
        using var sc2 = _swapChain.QueryInterface<IDXGISwapChain2>();
        sc2.MatrixTransform = Matrix3x2.CreateScale(1 / scaleX, 1 / scaleY);
    }

    void CreateTargets()
    {
        var format = Format.B8G8R8A8_UNorm;
        uint quality = _device.CheckMultisampleQualityLevels(format, SampleCount);
        var samples = quality > 0 ? new SampleDescription(SampleCount, 0) : new SampleDescription(1, 0);
        _msaaColor = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = samples,
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget,
        });
        _msaaRtv = _device.CreateRenderTargetView(_msaaColor);
        _msaaDepth = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.D32_Float,
            SampleDescription = samples,
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.DepthStencil,
        });
        _msaaDsv = _device.CreateDepthStencilView(_msaaDepth);
    }

    void ReleaseTargets()
    {
        _msaaRtv?.Dispose(); _msaaRtv = null;
        _msaaDsv?.Dispose(); _msaaDsv = null;
        _msaaColor?.Dispose(); _msaaColor = null;
        _msaaDepth?.Dispose(); _msaaDepth = null;
    }

    /// <summary>Re-rasterizes the digit glyphs with the given font.</summary>
    public void SetGlyphFont(string family, int weight)
    {
        var images = GlyphRasterizer.RenderDigits(family, weight);
        _glyphSrv?.Dispose();
        _glyphs?.Dispose();
        _glyphs = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)GlyphRasterizer.Width,
            Height = (uint)GlyphRasterizer.Height,
            MipLevels = 0,
            ArraySize = 10,
            Format = Format.R8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            MiscFlags = ResourceOptionFlags.GenerateMips,
        });
        _glyphSrv = _device.CreateShaderResourceView(_glyphs);
        uint mipLevels = _glyphs.Description.MipLevels;
        for (int d = 0; d < 10; d++)
        {
            uint subresource = D3D11.CalculateSubResourceIndex(0, (uint)d, mipLevels);
            _context.UpdateSubresource(images[d], _glyphs, subresource, (uint)GlyphRasterizer.Width, 0);
        }
        _context.GenerateMips(_glyphSrv);
    }

    /// <summary>Loads (or clears, when null/empty) the background image. Returns false if it could not be decoded.</summary>
    public bool SetBackgroundImage(string? path)
    {
        _bgSrv?.Dispose(); _bgSrv = null;
        _bgImage?.Dispose(); _bgImage = null;
        if (string.IsNullOrWhiteSpace(path)) return true;
        if (!File.Exists(path)) return false;
        var decoded = GlyphRasterizer.DecodeImage(path);
        if (decoded is not { } img) return false;

        _bgImage = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)img.Width,
            Height = (uint)img.Height,
            MipLevels = 0,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm_SRgb,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            MiscFlags = ResourceOptionFlags.GenerateMips,
        });
        _bgSrv = _device.CreateShaderResourceView(_bgImage);
        _context.UpdateSubresource(img.Pixels, _bgImage, 0, (uint)img.Width * 4, 0);
        _context.GenerateMips(_bgSrv);
        _bgWidth = img.Width;
        _bgHeight = img.Height;
        return true;
    }

    /// <summary>Draws and presents a frame. Returns false if the device was lost and the renderer must be recreated.</summary>
    public bool Render(ClockScene scene, bool present = true)
    {
        if (_msaaRtv is null || _msaaDsv is null || _msaaColor is null) return true;

        float W = _width, H = _height;
        var layout = ClockLayout.Compute(W, H, scene.MarginPx, scene.SecondsScale, 1);
        float bigW = (float)layout.Tiles[0].Width;
        float bigH = bigW * TileHeight;

        // Camera: z = 0 maps exactly onto the window.
        float distance = H / 2 / MathF.Tan(FieldOfView / 2);
        var eye = new Vector3(0, 0, distance);
        var view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY);
        float bgZ = -BackgroundDepth * bigH;
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, W / H, distance * 0.2f, distance - bgZ + distance * 0.5f);

        // Light: direction set by the angle in the screen plane, 50° above the clock face.
        float alpha = (float)(scene.LightAngle * Math.PI / 180), elevation = 50f * MathF.PI / 180f;
        var toLight = Vector3.Normalize(new Vector3(MathF.Cos(elevation) * MathF.Cos(alpha), MathF.Cos(elevation) * MathF.Sin(alpha), MathF.Sin(elevation)));
        float clockSpan = (float)(layout.Tiles[5].Right - layout.Tiles[0].X);
        float lightExtent = clockSpan * 1.35f + bigH;
        float lightDistance = lightExtent * 2;
        var lightView = Matrix4x4.CreateLookAt(toLight * lightDistance, Vector3.Zero, MathF.Abs(toLight.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY);
        var lightProj = Matrix4x4.CreateOrthographic(lightExtent, lightExtent, 1, lightDistance * 2);
        var lightViewProj = lightView * lightProj;

        float intensity = (float)scene.LightIntensity;
        var bgUv = BackgroundUv(W / H);
        var frame = new FrameConstants
        {
            ViewProj = view * proj,
            LightViewProj = lightViewProj,
            LightDir = new Vector4(toLight, 0),
            LightColor = new Vector4(new Vector3(1.0f, 0.97f, 0.92f) * 1.9f * intensity, Math.Clamp(0.55f * intensity, 0.2f, 0.8f)),
            CameraPos = new Vector4(eye, 1),
            DigitColor = Linear(scene.DigitColor),
            BgColor = new Vector4(Linear(scene.BackgroundColor).AsVector3(), (float)scene.BackgroundDim),
            BgUv = bgUv,
            Params = new Vector4(_bgSrv is null ? 0 : 1, 1f / ShadowMapSize, 0.32f, 0),
        };
        _context.UpdateSubresource(in frame, _frameCb);

        BuildDrawList(scene, layout, W, H);

        // Shared state.
        _context.IASetInputLayout(_layout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetConstantBuffers(0, [_frameCb, _objectCb]);
        _context.PSSetConstantBuffers(0, [_frameCb, _objectCb]);
        _context.OMSetDepthStencilState(_depthState);

        // Shadow pass.
        _context.PSSetShaderResources(0, [null!, null!, null!]);
        _context.OMSetRenderTargets((ID3D11RenderTargetView?)null, _shadowDsv);
        _context.ClearDepthStencilView(_shadowDsv, DepthStencilClearFlags.Depth, 1, 0);
        _context.RSSetViewport(new Viewport(ShadowMapSize, ShadowMapSize));
        _context.RSSetState(_shadowRasterizer);
        _context.VSSetShader(_vsShadow);
        _context.PSSetShader(null);
        foreach (var d in _draws) Draw(d);

        // Main pass.
        _context.OMSetRenderTargets(_msaaRtv, _msaaDsv);
        _context.ClearRenderTargetView(_msaaRtv, new Color4(0, 0, 0, 1));
        _context.ClearDepthStencilView(_msaaDsv, DepthStencilClearFlags.Depth, 1, 0);
        _context.RSSetViewport(new Viewport(W, H));
        _context.RSSetState(_rasterizer);
        _context.VSSetShader(_vs);
        _context.PSSetShaderResources(0, [_glyphSrv!, _shadowSrv, _bgSrv ?? _shadowSrv]);
        _context.PSSetSamplers(0, [_linearSampler, _shadowSampler]);

        // Background quad, scaled so it exactly covers the view at its depth.
        float cover = (distance - bgZ) / distance;
        _context.PSSetShader(_psBackground);
        Draw(new DrawItem(_quad, Matrix4x4.CreateScale(W * cover, H * cover, 1) * Matrix4x4.CreateTranslation(0, 0, bgZ), Vector4.One, Vector4.Zero));

        _context.PSSetShader(_ps);
        foreach (var d in _draws) Draw(d);

        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        if (_msaaColor.Description.SampleDescription.Count > 1)
            _context.ResolveSubresource(backBuffer, 0, _msaaColor, 0, Format.B8G8R8A8_UNorm);
        else
            _context.CopyResource(backBuffer, _msaaColor);

        if (!present) return true;
        var hr = _swapChain.Present(1, PresentFlags.None);
        return !(hr == Vortice.DXGI.ResultCode.DeviceRemoved || hr == Vortice.DXGI.ResultCode.DeviceReset);
    }

    /// <summary>Reads back the frame drawn by <c>Render(scene, present: false)</c> as tightly packed BGRA8 (for diagnostics and screenshots).</summary>
    public (byte[] Pixels, int Width, int Height) CaptureFrame()
    {
        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        var desc = backBuffer.Description;
        desc.Usage = ResourceUsage.Staging;
        desc.BindFlags = BindFlags.None;
        desc.CPUAccessFlags = CpuAccessFlags.Read;
        desc.MiscFlags = ResourceOptionFlags.None;
        using var staging = _device.CreateTexture2D(desc);
        _context.CopyResource(staging, backBuffer);
        var mapped = _context.Map(staging, 0, MapMode.Read);
        var pixels = new byte[_width * _height * 4];
        unsafe
        {
            for (int y = 0; y < _height; y++)
                new ReadOnlySpan<byte>((byte*)mapped.DataPointer + y * mapped.RowPitch, _width * 4)
                    .CopyTo(pixels.AsSpan(y * _width * 4));
        }
        _context.Unmap(staging, 0);
        return (pixels, _width, _height);
    }

    Vector4 BackgroundUv(float viewAspect)
    {
        if (_bgSrv is null || _bgHeight == 0) return new Vector4(1, 1, 0, 0);
        float imageAspect = (float)_bgWidth / _bgHeight;
        // Fill-crop: scale UVs down along the axis that overflows and center the crop.
        if (imageAspect > viewAspect)
        {
            float s = viewAspect / imageAspect;
            return new Vector4(s, 1, (1 - s) / 2, 0);
        }
        else
        {
            float s = imageAspect / viewAspect;
            return new Vector4(1, s, 0, (1 - s) / 2);
        }
    }

    void BuildDrawList(ClockScene scene, ClockLayoutResult layout, float W, float H)
    {
        _draws.Clear();
        float gloss = (float)scene.Glossiness;
        float roughness = 0.75f - 0.62f * gloss;
        var tileColor = Linear(scene.TileColor);
        var digitColor = Linear(scene.DigitColor);
        var axleColor = new Vector4(0.035f, 0.035f, 0.038f, 1);

        for (int i = 0; i < ClockDigits.Count; i++)
        {
            var rect = layout.Tiles[i];
            var v = scene.Tiles[i];
            float w = (float)rect.Width;
            var placement = Matrix4x4.CreateScale(w) * Matrix4x4.CreateTranslation((float)rect.CenterX - W / 2, H / 2 - (float)rect.CenterY, 0);

            _draws.Add(new DrawItem(_topHalf, placement, tileColor, new Vector4(roughness, 0, v.StaticTopDigit, -1)));
            _draws.Add(new DrawItem(_bottomHalf, placement, tileColor, new Vector4(roughness, 0, v.StaticBottomDigit, -1)));
            _draws.Add(new DrawItem(_axle,
                Matrix4x4.CreateScale(1.035f, AxleRadius, AxleRadius) * Matrix4x4.CreateTranslation(0, 0, SlabDepth * 0.5f) * placement,
                axleColor, new Vector4(0.4f, 1, -1, -1)));

            if (v.FlapVisible)
            {
                float pivotZ = SlabDepth + FlapDepth / 2;
                float angle = (float)(v.FlapAngle * Math.PI / 180);
                var flap = Matrix4x4.CreateTranslation(0, 0, -pivotZ) * Matrix4x4.CreateRotationX(angle) * Matrix4x4.CreateTranslation(0, 0, pivotZ);
                _draws.Add(new DrawItem(_flap, flap * placement, tileColor, new Vector4(roughness, 0, v.FlapFrontDigit, v.FlapBackDigit)));
            }
        }

        foreach (var colon in layout.Colons)
        {
            float r = (float)colon.DotRadius;
            foreach (var y in new[] { colon.TopDotY, colon.BottomDotY })
            {
                var world = Matrix4x4.CreateScale(r, r, r * 0.6f)
                            * Matrix4x4.CreateTranslation((float)colon.CenterX - W / 2, H / 2 - (float)y, SlabDepth * bigTileWidth(layout) * 0.55f);
                _draws.Add(new DrawItem(_sphere, world, digitColor, new Vector4(roughness * 0.9f, 0, -1, -1)));
            }
        }

        static float bigTileWidth(ClockLayoutResult l) => (float)l.Tiles[0].Width;
    }

    void Draw(in DrawItem item)
    {
        Matrix4x4.Invert(item.World, out var inverse);
        var constants = new ObjectConstants
        {
            World = item.World,
            NormalMatrix = Matrix4x4.Transpose(inverse),
            BaseColor = item.Color,
            Material = item.Material,
        };
        _context.UpdateSubresource(in constants, _objectCb);
        _context.IASetVertexBuffer(0, item.Mesh.Vertices, Vertex.Stride);
        _context.IASetIndexBuffer(item.Mesh.Indices, Format.R32_UInt, 0);
        _context.DrawIndexed(item.Mesh.IndexCount, 0, 0);
    }

    static Vector4 Linear(RgbColor c) => new(ToLinear(c.R), ToLinear(c.G), ToLinear(c.B), 1);

    static float ToLinear(byte v)
    {
        float c = v / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    public void Dispose()
    {
        ReleaseTargets();
        _glyphSrv?.Dispose(); _glyphs?.Dispose();
        _bgSrv?.Dispose(); _bgImage?.Dispose();
        _shadowSrv.Dispose(); _shadowDsv.Dispose(); _shadowMap.Dispose();
        foreach (var m in new[] { _topHalf, _bottomHalf, _flap, _sphere, _axle, _quad }) m.Dispose();
        _depthState.Dispose(); _rasterizer.Dispose(); _shadowRasterizer.Dispose();
        _linearSampler.Dispose(); _shadowSampler.Dispose();
        _frameCb.Dispose(); _objectCb.Dispose();
        _layout.Dispose(); _vs.Dispose(); _vsShadow.Dispose(); _ps.Dispose(); _psBackground.Dispose();
        _swapChain.Dispose();
        _context.ClearState();
        _context.Flush();
        _context.Dispose();
        _device.Dispose();
    }
}
