using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Compiler = Vortice.D3DCompiler.Compiler;

namespace ETS2_Assist_GUI.AR
{
    /// <summary>
    /// AR v2.0 — D3D11-рендерер (GPU-геометрия; никакого Canvas2D/DOM).
    /// Схема кадра (архитектурное правило): pose→predicт→GameState→маркер
    /// трансформы→GPU буферы→Render→Present. Никаких тяжёлых операций между.
    /// Flip model + waitable swap chain (SetMaximumFrameLatency(1)).
    /// </summary>
    public sealed class ArRenderer : IDisposable
    {
        private IDXGIFactory2? _factory;
        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGISwapChain1? _swapChain;
        private ID3D11RenderTargetView? _rtv;

        // DirectComposition (v100): device/target/visual для per-pixel alpha.
        private object? _dcompDevice;
        private object? _dcompTarget;
        private object? _dcompVisual;

        private int _width, _height;

        /// <summary>Прогноз задержки pipeline, мс (подбирается экспериментально).</summary>
        public float PredictionSeconds { get; set; } = 0.006f;

        public void Initialize(IntPtr hwnd, int width, int height)
        {
            _width = width; _height = height;
            _factory = DXGI.CreateDXGIFactory2<IDXGIFactory2>(false);

            // D3D11 (не D3D12: проще, для HUD возможностей достаточно).
            var flags = DeviceCreationFlags.BgraSupport;
            D3D11.D3D11CreateDevice(
                null, DriverType.Hardware, flags, new[]
                {
                    FeatureLevel.Level_11_1, FeatureLevel.Level_11_0
                }, out _device!, out _context!);

            // v100: COMPOSITION swap chain (DirectComposition per-pixel alpha).
            // CreateSwapChainForComposition требует flip model + premultiplied alpha.
            // COLORKEY/bitblt (SwapEffect.Sequential + AlphaMode.Ignore) УДАЛЕНЫ.
            var sc = new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = Format.B8G8R8A8_UNorm,
                Stereo = false,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipSequential,   // composition требует flip
                AlphaMode = AlphaMode.Premultiplied,      // premultiplied alpha
                Flags = SwapChainFlags.None
            };
            _swapChain = _factory.CreateSwapChainForComposition(_device, sc, null);

            using (ID3D11Texture2D back = _swapChain.GetBuffer<ID3D11Texture2D>(0))
                _rtv = _device.CreateRenderTargetView(back);

            // v100: DirectComposition — device/target/visual/commit.
            InitDirectComposition(hwnd);

            // Шейдеры маркера (inline HLSL, компиляция на старте; см. маркер.hlsl план).
            CreateMarkerPipeline();
            CreateTextPipeline();   // v83: текстовый конвейер (имя/дистанция/углы)
            CreateBoxPipeline();    // v85: боксовый конвейер (центр. пиксель, линии креста pin)
            CreateCubePipeline();   // v95: 3D-куб (ориентация головы)
            CreateLinePipeline();   // v96: линии (3D-сетка плоскости)
            CreateEllipsePipeline();// v96: мягкие эллипсы (тень точки на плоскости)
        }

        // ================================================================
        // v100: DirectComposition initialization (per-pixel alpha).
        // D3D11 device → IDXGIDevice → DCompositionCreateDevice →
        // CreateTargetForHwnd → CreateVisual → SetContent(swapChain) →
        // SetRoot(visual) → Commit.
        // ================================================================
        private void InitDirectComposition(IntPtr hwnd)
        {
            using var dxgiDevice = _device!.QueryInterface<Vortice.DXGI.IDXGIDevice>();
            // v100: передаём НАТИВНЫЙ IDXGIDevice* (NativePointer), а не SharpGen-обёртку —
            // иначе DCompositionCreateDevice вернёт E_NOINTERFACE (0x80004002).
            _dcompDevice = DirectComposition.CreateDevice(dxgiDevice.NativePointer);
            _dcompTarget = DirectComposition.CreateTargetForHwnd(_dcompDevice, hwnd, true);
            _dcompVisual = DirectComposition.CreateVisual(_dcompDevice);
            DirectComposition.SetContent(_dcompVisual, _swapChain!.NativePointer);
            DirectComposition.SetRoot(_dcompTarget, _dcompVisual);
            DirectComposition.Commit(_dcompDevice);
        }

        // ---------- МАРКЕР v2.0 (первый GPU-элемент) ----------
        // v95: СПЛОШНАЯ ТОЧКА (без отверстия) с чёрной ПОЛУПРОЗРАЧНОЙ обводкой
        // 3px, цвет = категория, альфа по дистанции. Радиус уже ×2 меньше (C#).
        private const string MarkerHlsl = """
            struct VS_IN { float2 pos : POS; };
            struct VS_OUT { float4 pos : SV_POSITION; float2 uv : UV; float2 px : PX; };
            cbuffer Screen : register(b0) { float2 Viewport; float2 _pad; };
            cbuffer Params : register(b1) { float4 Circle; float4 Tint; }; /* Circle: xy=center px, z=radius px, w=alpha; Tint: rgb */
            VS_OUT VMain(VS_IN i) {
                VS_OUT o;
                float2 px = Circle.xy + (i.pos - 0.5) * 2 * Circle.z;
                float2 ndc = float2(px.x / Viewport.x * 2 - 1, 1 - px.y / Viewport.y * 2);
                o.pos = float4(ndc, 0, 1);
                o.uv = i.pos - 0.5;
                o.px = px;
                return o;
            }
            float4 PMain(VS_OUT i) : SV_TARGET {
                float dpx = distance(i.px, Circle.xy);   // пикселей от центра
                float R = Circle.z;
                // Точка (сплошная, цвет категории): сглаженный край.
                float body = smoothstep(R + 0.5, R - 1.0, dpx);
                // Чёрная ПОЛУПРОЗРАЧНАЯ обводка 3px (0.6 альфы).
                float ring = smoothstep(R - 3.5, R - 2.5, dpx) * smoothstep(R + 0.5, R - 1.0, dpx);
                float3 col = lerp(Tint.rgb, float3(0, 0, 0), ring);
                float a = saturate(Circle.w * (body * 1.0 + ring * 0.6));
                // v100: premultiplied alpha (RGB*A) — для composition swap chain.
                return float4(col * a, a);
            }
            """;

        private ID3D11VertexShader? _vs;
        private ID3D11PixelShader? _ps;
        private ID3D11InputLayout? _il;
        private ID3D11Buffer? _vb, _cbScreen, _cbParams;
        private ID3D11BlendState? _blend;

        private void CreateMarkerPipeline()
        {
            // Компиляция HLSL: Blob(out код, out ошибки) — при ошибке пишем в лог.
            var vsRes = Compiler.Compile(MarkerHlsl, "VMain", "marker", "vs_5_0",
                out var vsBlob, out var vsErr);
            if (vsRes.Failure || vsBlob == null)
                throw new InvalidOperationException("VS compile: " + (vsErr != null ? System.Text.Encoding.UTF8.GetString(vsBlob == null ? vsErr.AsSpan() : Array.Empty<byte>()) : vsRes.ToString()));
            var psRes = Compiler.Compile(MarkerHlsl, "PMain", "marker", "ps_5_0",
                out var psBlob, out var psErr);
            if (psRes.Failure || psBlob == null)
                throw new InvalidOperationException("PS compile: " + (psErr != null ? System.Text.Encoding.UTF8.GetString(psErr.AsSpan()) : psRes.ToString()));

            _vs = _device!.CreateVertexShader(vsBlob, null);
            _ps = _device.CreatePixelShader(psBlob, null);

            // Полный экран-квад (два треугольника), UV [0..1].
            var verts = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1),
                new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1),
            };
            _vb = _device.CreateBuffer<Vector2>(verts,
                BindFlags.VertexBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None);

            _il = _device.CreateInputLayout(new[]
            {
                new InputElementDescription("POS", 0, Format.R32G32_Float, 0, 0)
            }, vsBlob);

            _cbScreen = _device.CreateBuffer(new BufferDescription(
                16, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
            // v85: 32 байта — (u, v, radius, alpha) + (R, G, B, pad).
            _cbParams = _device.CreateBuffer(new BufferDescription(
                32, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));

            var bd = new BlendDescription
            {
                AlphaToCoverageEnable = false,
                IndependentBlendEnable = false
            };
            bd.RenderTarget[0] = new RenderTargetBlendDescription
            {
                BlendEnable = true,
                // v100: premultiplied alpha — shader возвращает RGB*A, поэтому Src=ONE.
                SourceBlend = Blend.One,
                DestinationBlend = Blend.InverseSourceAlpha,
                BlendOperation = BlendOperation.Add,
                SourceBlendAlpha = Blend.One,
                DestinationBlendAlpha = Blend.InverseSourceAlpha,
                BlendOperationAlpha = BlendOperation.Add,
                RenderTargetWriteMask = ColorWriteEnable.All
            };
            _blend = _device.CreateBlendState(bd);
        }

        // ==================== БОКСОВАЯ ОТРИСОВКА (v85) ====================
        // Одноцветный прямоугольник (прицельный центр. пиксель, линии креста pin).
        private const string BoxHlsl = """
            struct VS_IN { float2 pos : POS; };
            struct VS_OUT { float4 pos : SV_POSITION; float2 uv : UV; };
            cbuffer Screen : register(b0) { float2 Viewport; float2 _pad; };
            cbuffer Rect  : register(b1) { float4 R; /* xy=top-left px, zw=size px */ float4 Tint; /* rgb + alpha */ };
            VS_OUT BMain(VS_IN i) {
                VS_OUT o;
                float2 px = R.xy + i.pos * R.zw;
                o.pos = float4(px.x / Viewport.x * 2 - 1, 1 - px.y / Viewport.y * 2, 0, 1);
                o.uv = i.pos;
                return o;
            }
            float4 BMainP(VS_OUT i) : SV_TARGET {
                // v100: premultiplied alpha.
                float a = saturate(Tint.w);
                return float4(Tint.rgb * a, a);
            }
            """;
        private ID3D11VertexShader? _bvs;
        private ID3D11PixelShader? _bps;
        private ID3D11InputLayout? _bil;
        private ID3D11Buffer? _bvb, _bcb;

        private void CreateBoxPipeline()
        {
            var vsRes = Compiler.Compile(BoxHlsl, "BMain", "box", "vs_5_0", out var vsBlob, out _);
            if (vsRes.Failure || vsBlob == null) return;
            var psRes = Compiler.Compile(BoxHlsl, "BMainP", "box", "ps_5_0", out var psBlob, out _);
            if (psRes.Failure || psBlob == null) return;
            _bvs = _device!.CreateVertexShader(vsBlob, null);
            _bps = _device.CreatePixelShader(psBlob, null);
            _bil = _device.CreateInputLayout(new[]
            {
                new InputElementDescription("POS", 0, Format.R32G32_Float, 0, 0)
            }, vsBlob);
            var verts = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1),
                new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1),
            };
            _bvb = _device.CreateBuffer<Vector2>(verts, BindFlags.VertexBuffer,
                ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None);
            _bcb = _device.CreateBuffer(new BufferDescription(
                32, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }

        // Метка цели: КРУГ с чёрной обводкой и тёмным центром — как drawMarkerToward JS.
        private void DrawCircle(float u, float v, float radius, float r, float g, float b, float a)
        {
            if (_vs == null || _ps == null || _il == null || _vb == null || _cbScreen == null ||
                _cbParams == null || _blend == null) return;
            var ctx = _context!;
            unsafe
            {
                var mp0 = ctx.Map(_cbScreen!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                var p0 = (float*)mp0.DataPointer;
                p0[0] = _width; p0[1] = _height; p0[2] = 0f; p0[3] = 0f;
                ctx.Unmap(_cbScreen!);
                var mp1 = ctx.Map(_cbParams!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                var p1 = (float*)mp1.DataPointer;
                p1[0] = u; p1[1] = v; p1[2] = radius; p1[3] = a;
                p1[4] = r; p1[5] = g; p1[6] = b; p1[7] = 0f;
                ctx.Unmap(_cbParams!);
            }
            ctx.IASetInputLayout(_il);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.IASetVertexBuffer(0, _vb, 8, 0);
            ctx.VSSetShader(_vs);
            ctx.VSSetConstantBuffer(0, _cbScreen);
            ctx.VSSetConstantBuffer(1, _cbParams);
            ctx.PSSetShader(_ps!);
            ctx.PSSetConstantBuffer(0, _cbScreen);
            ctx.PSSetConstantBuffer(1, _cbParams);
            ctx.OMSetBlendState(_blend);
            ctx.OMSetRenderTargets(_rtv, null);
            ctx.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, _width, _height));
            ctx.Draw(6, 0);
        }

        // Одноцветный прямоугольник (x, y — ЛЕВЫЙ ВЕРХНИЙ угол, px).
        private void DrawBox(float x, float y, float w, float h, float r, float g, float b, float a)
        {
            if (_bvs == null || _bps == null || _bil == null || _bvb == null || _bcb == null) return;
            var ctx = _context!;
            unsafe
            {
                var mp0 = ctx.Map(_cbScreen!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                var p0 = (float*)mp0.DataPointer;
                p0[0] = _width; p0[1] = _height; p0[2] = 0f; p0[3] = 0f;
                ctx.Unmap(_cbScreen!);
                var mp = ctx.Map(_bcb!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                var p = (float*)mp.DataPointer;
                p[0] = x; p[1] = y; p[2] = w; p[3] = h;
                p[4] = r; p[5] = g; p[6] = b; p[7] = a;
                ctx.Unmap(_bcb!);
            }
            ctx.IASetInputLayout(_bil);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.IASetVertexBuffer(0, _bvb, 8, 0);
            ctx.VSSetShader(_bvs);
            ctx.VSSetConstantBuffer(0, _cbScreen);
            ctx.VSSetConstantBuffer(1, _bcb);
            ctx.PSSetShader(_bps!);
            ctx.PSSetConstantBuffer(0, _cbScreen);
            ctx.PSSetConstantBuffer(1, _bcb);
            ctx.OMSetBlendState(_tblend);
            ctx.OMSetRenderTargets(_rtv, null);
            ctx.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, _width, _height));
            ctx.Draw(6, 0);
        }

        // ==================== ЛИНИИ (v96: 3D-сетка плоскости) ====================
        // Отрисовка отрезков (LineList) с заданным цветом/альфой. Вершины в NDC.
        private const string LineHlsl = """
            struct VS_IN { float4 pos : POS; float4 col : COL; };
            struct VS_OUT { float4 pos : SV_POSITION; float4 col : COL; };
            VS_OUT LMain(VS_IN i) {
                VS_OUT o;
                o.pos = i.pos;      // NDC уже посчитан на C#
                o.col = i.col;
                return o;
            }
            float4 LMainP(VS_OUT i) : SV_TARGET {
                // v100: premultiplied alpha.
                float a = saturate(i.col.a);
                return float4(i.col.rgb * a, a);
            }
            """;
        private ID3D11VertexShader? _lvs;
        private ID3D11PixelShader? _lps;
        private ID3D11InputLayout? _lil;
        private ID3D11Buffer? _lvb;

        private void CreateLinePipeline()
        {
            var vs = Compiler.Compile(LineHlsl, "LMain", "line", "vs_5_0", out var vsBlob, out _);
            var ps = Compiler.Compile(LineHlsl, "LMainP", "line", "ps_5_0", out var psBlob, out _);
            if (vs.Failure || vsBlob == null || ps.Failure || psBlob == null) return;
            _lvs = _device!.CreateVertexShader(vsBlob, null);
            _lps = _device.CreatePixelShader(psBlob, null);
            _lil = _device.CreateInputLayout(new[]
            {
                new InputElementDescription("POS", 0, Format.R32G32B32A32_Float, 0, 0),
                new InputElementDescription("COL", 0, Format.R32G32B32A32_Float, 16, 0),
            }, vsBlob);
            _lvb = _device.CreateBuffer(new BufferDescription(
                4096 * 32, BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }

        // Отрисовка набора отрезков (TriangleList, 6 вершин на отрезок = 2 треуг.).
        // Толщина линий 3px достигается расширением отрезка в прямоугольник
        // (D3D11 не поддерживает LineWidth в растеризаторе). verts — float[count*6*8].
        private void DrawLines(float[] verts, int count)
        {
            if (_lvs == null || _lps == null || _lil == null || _lvb == null || count <= 0) return;
            var ctx = _context!;
            unsafe
            {
                var mp = ctx.Map(_lvb!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                System.Runtime.InteropServices.Marshal.Copy(verts, 0, mp.DataPointer, count * 6 * 8);
                ctx.Unmap(_lvb!);
            }
            ctx.IASetInputLayout(_lil);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.IASetVertexBuffer(0, _lvb, 32, 0);
            ctx.VSSetShader(_lvs);
            ctx.PSSetShader(_lps!);
            ctx.OMSetBlendState(_tblend);
            ctx.OMSetRenderTargets(_rtv, null);
            ctx.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, _width, _height));
            ctx.Draw((uint)(count * 6), 0);
        }

        // ==================== ЭЛЛИПСЫ (v96: мягкая тень точки на плоскости) ====================
        // Мягкий овал (псевдо-тень) с затуханием альфы к краю. Центр/радиусы в px.
        private const string EllipseHlsl = """
            struct VS_IN { float2 pos : POS; };
            struct VS_OUT { float4 pos : SV_POSITION; float2 uv : UV; float2 px : PX; };
            cbuffer Screen : register(b0) { float2 Viewport; float2 _pad; };
            cbuffer Params : register(b1) { float4 Ell; /* xy=center px, z=rx, w=ry */ float4 Tint; /* rgb + alpha */ };
            VS_OUT EMain(VS_IN i) {
                VS_OUT o;
                float2 px = Ell.xy + (i.pos - 0.5) * 2 * float2(Ell.z, Ell.w);
                float2 ndc = float2(px.x / Viewport.x * 2 - 1, 1 - px.y / Viewport.y * 2);
                o.pos = float4(ndc, 0, 1);
                o.uv = i.pos - 0.5;
                o.px = px;
                return o;
            }
            float4 EMainP(VS_OUT i) : SV_TARGET {
                float2 d = (i.px - Ell.xy) / float2(Ell.z, Ell.w);   // нормализованное расстояние
                float r = length(d);
                float a = smoothstep(1.0, 0.35, r);   // мягкий край (затухание к центру)
                a = saturate(Tint.w * a);
                // v100: premultiplied alpha.
                return float4(Tint.rgb * a, a);
            }
            """;
        private ID3D11VertexShader? _evs;
        private ID3D11PixelShader? _eps;
        private ID3D11InputLayout? _eil;
        private ID3D11Buffer? _evb, _ecb;

        private void CreateEllipsePipeline()
        {
            var vs = Compiler.Compile(EllipseHlsl, "EMain", "ellipse", "vs_5_0", out var vsBlob, out _);
            var ps = Compiler.Compile(EllipseHlsl, "EMainP", "ellipse", "ps_5_0", out var psBlob, out _);
            if (vs.Failure || vsBlob == null || ps.Failure || psBlob == null) return;
            _evs = _device!.CreateVertexShader(vsBlob, null);
            _eps = _device.CreatePixelShader(psBlob, null);
            _eil = _device.CreateInputLayout(new[]
            {
                new InputElementDescription("POS", 0, Format.R32G32_Float, 0, 0)
            }, vsBlob);
            var verts = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1),
                new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1),
            };
            _evb = _device.CreateBuffer<Vector2>(verts, BindFlags.VertexBuffer,
                ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None);
            _ecb = _device.CreateBuffer(new BufferDescription(
                32, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }

        // Мягкий овал (псевдо-тень) с центром (u,v), радиусами rx/ry, цветом и альфой.
        private void DrawEllipse(float u, float v, float rx, float ry, float r, float g, float b, float a)
        {
            if (_evs == null || _eps == null || _eil == null || _evb == null || _ecb == null) return;
            var ctx = _context!;
            unsafe
            {
                var mp0 = ctx.Map(_cbScreen!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                var p0 = (float*)mp0.DataPointer;
                p0[0] = _width; p0[1] = _height; p0[2] = 0f; p0[3] = 0f;
                ctx.Unmap(_cbScreen!);
                var mp = ctx.Map(_ecb!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                var p = (float*)mp.DataPointer;
                p[0] = u; p[1] = v; p[2] = rx; p[3] = ry;
                p[4] = r; p[5] = g; p[6] = b; p[7] = a;
                ctx.Unmap(_ecb!);
            }
            ctx.IASetInputLayout(_eil);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.IASetVertexBuffer(0, _evb, 8, 0);
            ctx.VSSetShader(_evs);
            ctx.VSSetConstantBuffer(0, _cbScreen);
            ctx.VSSetConstantBuffer(1, _ecb);
            ctx.PSSetShader(_eps!);
            ctx.PSSetConstantBuffer(0, _cbScreen);
            ctx.PSSetConstantBuffer(1, _ecb);
            ctx.OMSetBlendState(_tblend);
            ctx.OMSetRenderTargets(_rtv, null);
            ctx.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, _width, _height));
            ctx.Draw(6, 0);
        }

        // Цвет метки: как в редакторе карт / CATEGORY_COLORS ar_hud.js (v85).
        private static (float r, float g, float b) ColorFor(ArMarker t)
        {
            if (!string.IsNullOrEmpty(t.Color) && t.Color.StartsWith("#") && t.Color.Length == 7)
            {
                try
                {
                    byte cr = Convert.ToByte(t.Color.Substring(1, 2), 16);
                    byte cg = Convert.ToByte(t.Color.Substring(3, 2), 16);
                    byte cb = Convert.ToByte(t.Color.Substring(5, 2), 16);
                    return (cr / 255f, cg / 255f, cb / 255f);
                }
                catch { }
            }
            switch (t.Category)
            {
                case "Company": return (0xff / 255f, 0x78 / 255f, 0xc8 / 255f);
                case "BusStop": return (0x78 / 255f, 0xdc / 255f, 0xff / 255f);
                case "Ferry": return (0x78 / 255f, 0xff / 255f, 0xb4 / 255f);
                case "Fuel": return (0xff / 255f, 0xc8 / 255f, 0x50 / 255f);
                case "Garage": return (0xb4 / 255f, 0xa0 / 255f, 0xff / 255f);
                case "Overlay": return (0xc8 / 255f, 0xc8 / 255f, 0xc8 / 255f);
                case "Parking": return (0xff / 255f, 0xa0 / 255f, 0x5a / 255f);
                case "Recruitment": return (0xff / 255f, 0x78 / 255f, 0x78 / 255f);
                case "Service": return (0x78 / 255f, 0xff / 255f, 0xff / 255f);
                case "Train": return (0xa0 / 255f, 0xc8 / 255f, 0xff / 255f);
                case "TruckDealer": return (0xff / 255f, 0xdc / 255f, 0x78 / 255f);
                case "WeightStation": return (0xdc / 255f, 0xb4 / 255f, 0xff / 255f);
                case "Город": return (1f, 1f, 0x5c / 255f);
                default: break;
            }
            return t.Kind switch
            {
                "target" => (1f, 0x3b / 255f, 0x30 / 255f),
                "city" => (1f, 1f, 0x5c / 255f),
                _ => (0x70 / 255f, 0xd1 / 255f, 0xfe / 255f)
            };
        }

        private static string FmtDist(double d)
            => d < 1000 ? Math.Round(d) + " м"
                        : (d / 1000.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " км";

        // ================================================================
        // v95: ПРОБА 3D-ОБЪЕКТА — зелёный куб с чёрными гранями в правом
        // нижнем углу. Вращается строго по ориентации ГОЛОВЫ (yaw+pitch).
        // Свет ровно сверху (освещение граней по углу к +Y).
        // Реализация: треугольниковый пайплайн (CPU поворот/проекция).
        // Оболочка чёрная (1.045×) → внутри зелёные грани (1.0×, освещённые):
        // так получаем чёрные грани/кант. Источник света сверху.
        // ================================================================
        private const string CubeHlsl = """
            struct VS_IN { float4 pos : POS; float4 col : COL; };
            struct VS_OUT { float4 pos : SV_POSITION; float4 col : COL; };
            VS_OUT VMain(VS_IN i) {
                VS_OUT o;
                o.pos = float4(i.pos.xy, 0, 1);   // NDC уже посчитан на C#
                o.col = i.col;
                return o;
            }
            // v100: per-pixel alpha (premultiplied). Bayer/dither УДАЛЁН —
            // DirectComposition swap chain поддерживает настоящую прозрачность.
            float4 PMain(VS_OUT i) : SV_TARGET {
                float a = saturate(i.col.a);
                return float4(i.col.rgb * a, a);
            }
            """;
        private ID3D11VertexShader? _cvsh;
        private ID3D11PixelShader? _cpsh;
        private ID3D11InputLayout? _cil;
        private ID3D11Buffer? _cvb;
        private const int CubeVerts = 36;   // 12 треугольников × 3
        private ID3D11Buffer? _planeVb;     // v40: буфер заливки плоскости земли (прямоугольник = 2 треуг.)
        private const int PlaneVerts = 6;

        private void CreateCubePipeline()
        {
            var vs = Compiler.Compile(CubeHlsl, "VMain", "cube", "vs_5_0", out var vsBlob, out _);
            var ps = Compiler.Compile(CubeHlsl, "PMain", "cube", "ps_5_0", out var psBlob, out _);
            if (vs.Failure || vsBlob == null || ps.Failure || psBlob == null) return;
            _cvsh = _device!.CreateVertexShader(vsBlob, null);
            _cpsh = _device.CreatePixelShader(psBlob, null);
            _cil = _device.CreateInputLayout(new[]
            {
                new InputElementDescription("POS", 0, Format.R32G32B32A32_Float, 0, 0),
                new InputElementDescription("COL", 0, Format.R32G32B32A32_Float, 16, 0),
            }, vsBlob);
            _cvb = _device.CreateBuffer(new BufferDescription(
                (uint)(CubeVerts * 32), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
            _planeVb = _device.CreateBuffer(new BufferDescription(
                (uint)(PlaneVerts * 32), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }

        private void DrawHeadCube(ArGameState? s)
        {
            if (s == null || _cvsh == null || _cpsh == null || _cil == null || _cvb == null) return;

            // Базовая геометрия куба ±1 и 6 граней (по 2 треуг. каждый).
            (float x, float y, float z)[] corner = {
                (-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
                (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1) };
            int[][] faceTris = {
                new int[]{0,1,2, 0,2,3}, new int[]{5,4,7, 5,7,6}, // -Z,+Z
                new int[]{0,3,7, 0,7,4}, new int[]{1,5,6, 1,6,2}, // -X,+X
                new int[]{0,4,5, 0,5,1}, new int[]{3,2,6, 3,6,7}, // -Y,+Y
            };
            // Нормали граней (±X,±Y,±Z) для освещения сверху.
            (float nx,float ny,float nz)[] fn = {
                (0,0,-1),(0,0,1),(-1,0,0),(1,0,0),(0,-1,0),(0,1,0) };

            double yaw = -s.YawHead * Math.PI * 2;   // v96: инверсия — голова вправо → куб вправо
            double pitch = s.PitchHead * Math.PI * 2;
            double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
            double cp = Math.Cos(pitch), sp = Math.Sin(pitch);

            // Сборка вершины: поворот (yaw→pitch), мир→экран, NDC, цвет.
            float[] vb = new float[CubeVerts * 8];
            int o = 0;
            for (int f = 0; f < 6; f++)
            {
                var n = fn[f];
                // Свет сверху: освещение = 0.35 + 0.65*max(0, n·L), L=(0,1,0).
                double light = 0.35 + 0.65 * Math.Max(0, n.ny);
                float g = (float)(0.35 * light), gg2 = (float)(0.75 * light), bg = (float)(0.25 * light);
                // Чуть темнее на −X/−Z (боковые тени).
                double side = 1.0 - 0.25 * Math.Max(0, -n.nx - n.nz);
                g *= (float)side; gg2 *= (float)side; bg *= (float)side;
                for (int t = 0; t < 6; t++)
                {
                    int vi = faceTris[f][t];
                    double x = corner[vi].x, y = corner[vi].y, z = corner[vi].z;
                    // yaw вокруг Y.
                    double x1 = x * cy + z * sy, z1 = -x * sy + z * cy;
                    // pitch вокруг X.
                    double y2 = y * cp - z1 * sp, z2 = y * sp + z1 * cp;
                    // Ортографическая проекция в экранные px (правый нижний угол).
                    // z задаёт «квази-глубину» для лёгкого перспективного масштаба.
                    double persp = 1.0 + z2 * 0.06;    // ближе = чуть крупнее
                    float sx = (float)(x1 * 46 * persp);
                    float sy2 = (float)(y2 * 39 * persp);
                    float nx2 = 2.0f * ((sx + 110f) / _width) - 1.0f;
                    float ny2 = 1.0f - 2.0f * ((sy2 + 110f) / _height);
                    vb[o++] = nx2; vb[o++] = ny2; vb[o++] = 0; vb[o++] = 0;
                    vb[o++] = g; vb[o++] = gg2; vb[o++] = bg; vb[o++] = 1f;
                }
            }

            var ctx = _context!;
            unsafe
            {
                var mp = ctx.Map(_cvb!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                System.Runtime.InteropServices.Marshal.Copy(vb, 0, mp.DataPointer, vb.Length);
                ctx.Unmap(_cvb!);
            }
            ctx.IASetInputLayout(_cil);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.IASetVertexBuffer(0, _cvb, 32, 0);
            ctx.VSSetShader(_cvsh);
            ctx.PSSetShader(_cpsh!);
            ctx.OMSetBlendState(_tblend);
            ctx.OMSetRenderTargets(_rtv, null);
            ctx.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, _width, _height));
            ctx.Draw(CubeVerts, 0);
        }

        // ================================================================
        // v40: ЗАЛИВКА ПЛОСКОСТИ ЗЕМЛИ ПОДОБНО КУБУ (CPU-пайплайн).
        // Рисуем большой прямоугольник на плоскости Y=planeY через ту же
        // CUBE-проекцию (ProjectPoint + NDC), цвет/альфа задаём на C#.
        // НАДЁЖНО виден (как куб), полупрозрачность через _tblend.
        // ================================================================
        private void DrawPlaneFillCpu(ArGameState? s, double planeY)
        {
            if (s == null || _cvsh == null || _cpsh == null || _cil == null || _planeVb == null) return;
            const double RadiusM = 180.0;

            // Четыре угла плоскости проецируем через ProjectPoint (мировые → экран px).
            var c = new (float x, float y, bool ok)[4];
            c[0] = Proj(s, planeY, -RadiusM, -RadiusM);
            c[1] = Proj(s, planeY,  RadiusM, -RadiusM);
            c[2] = Proj(s, planeY,  RadiusM,  RadiusM);
            c[3] = Proj(s, planeY, -RadiusM,  RadiusM);
            if (!c[0].ok || !c[1].ok || !c[2].ok || !c[3].ok) return;

            // 2 треугольника: (0,1,2) и (0,2,3). NDC уже на C# (как куб).
            float[] vb = new float[PlaneVerts * 8];
            int o = 0;
            // Полупрозрачный оранжевый (как сетка), хорошо заметен на прозрачном окне.
            float pr = 1.0f, pg = 0.62f, pb = 0.0f, pa = 0.38f;
            void Emit(float x, float y)
            {
                float nx = 2.0f * (x / _width) - 1.0f;
                float ny = 1.0f - 2.0f * (y / _height);
                vb[o++] = nx; vb[o++] = ny; vb[o++] = 0; vb[o++] = 1f;
                vb[o++] = pr; vb[o++] = pg; vb[o++] = pb; vb[o++] = pa;
            }
            Emit(c[0].x, c[0].y);
            Emit(c[1].x, c[1].y);
            Emit(c[2].x, c[2].y);
            Emit(c[0].x, c[0].y);
            Emit(c[2].x, c[2].y);
            Emit(c[3].x, c[3].y);

            var ctx = _context!;
            unsafe
            {
                var mp = ctx.Map(_planeVb!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                System.Runtime.InteropServices.Marshal.Copy(vb, 0, mp.DataPointer, vb.Length);
                ctx.Unmap(_planeVb!);
            }
            ctx.IASetInputLayout(_cil);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.IASetVertexBuffer(0, _planeVb, 32, 0);
            ctx.VSSetShader(_cvsh);
            ctx.PSSetShader(_cpsh!);
            ctx.OMSetBlendState(_tblend);
            ctx.OMSetRenderTargets(_rtv, null);
            ctx.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, _width, _height));
            ctx.Draw(PlaneVerts, 0);
        }

        // ================================================================
        // v40.1: ОТЛАДОЧНЫЙ КУБ (ПЛИТА) НА ПЛОСКОСТИ ЗЕМЛИ — МИР → ЭКРАН.
        // Причина многодневной «невидимости плоскости»: полупрозрачные линии/
        // заливки на COLORKEY-окне блендятся с ЧЁРНЫМ фоном → почти чёрный
        // цвет ≈ ключ прозрачности → невидимы. Куб головы виден, потому что
        // он ЯРКИЙ и НЕПРОЗРАЧНЫЙ. Поэтому отладка проекции плоскости = яркая
        // НЕпрозрачная плита в мире (40×40×1 м) на высоте planeY, 25 м впереди.
        // Тот же пайплайн, что куб головы (_cvsh/_cpsh/_cil) — гарантия видимости.
        // ================================================================
        private ID3D11Buffer? _gcubeVb;   // верш. буфер отладочной плиты (36 верш.)
        private const int GCubeVerts = 36;

        private void DrawGroundDebugCube(ArGameState? s)
        {
            if (s == null || _cvsh == null || _cpsh == null || _cil == null) return;
            double planeY = s.GroundY + s.PlaneOffsetM;

            // Центр плиты: 25 м вперёд по yaw (кузов+голова) — как pin-луч.
            double yaw = s.YawBase * Math.PI * 2 + s.YawHead * Math.PI * 2;
            double fx = -Math.Sin(yaw), fz = -Math.Cos(yaw);
            double cx = s.CamX + fx * 25.0, cz = s.CamZ + fz * 25.0;

            const double half = 20.0;   // плита 40×40 м
            const double h = 1.0;       // высота 1 м — «плоскость»
            double yTop = planeY, yBot = planeY - h;

            // 8 углов (та же нумерация, что DrawHeadCube: y=±1 низ/верх).
            (double x, double y, double z)[] c = {
                (cx-half,yBot,cz-half),(cx+half,yBot,cz-half),(cx+half,yTop,cz-half),(cx-half,yTop,cz-half),
                (cx-half,yBot,cz+half),(cx+half,yBot,cz+half),(cx+half,yTop,cz+half),(cx-half,yTop,cz+half) };

            // Мир → экран (px) через ту же проекцию, что метки.
            var scr = new (float u, float v, bool ok)[8];
            for (int i = 0; i < 8; i++)
            {
                var p = ProjectPoint(c[i].x, c[i].y, c[i].z, s);
                if (p == null || !p.Value.inFront) return;   // плита за спиной — не рисуем
                scr[i] = (p.Value.u, p.Value.v, true);
            }

            int[][] faceTris = {
                new int[]{0,1,2, 0,2,3}, new int[]{5,4,7, 5,7,6}, // -Z,+Z
                new int[]{0,3,7, 0,7,4}, new int[]{1,5,6, 1,6,2}, // -X,+X
                new int[]{0,4,5, 0,5,1}, new int[]{3,2,6, 3,6,7}, // -Y,+Y
            };
            // Порядок граней: дальние раньше (painter, depth-буфера нет).
            var order = new int[6] { 0, 1, 2, 3, 4, 5 };
            double FaceDepth(int f)
            {
                double d = 0;
                for (int t = 0; t < 4; t++)
                {
                    int vi = faceTris[f][t];
                    double dx = c[vi].x - s.CamX, dy = c[vi].y - s.CamY, dz = c[vi].z - s.CamZ;
                    d += Math.Sqrt(dx * dx + dy * dy + dz * dz);
                }
                return d;
            }
            var depths = new double[6];
            for (int f = 0; f < 6; f++) depths[f] = FaceDepth(f);
            Array.Sort(depths, order);   // дальние (большая глубина) первыми

            float[] vb = new float[GCubeVerts * 8];
            int o = 0;
            // Ярко-зелёный НЕпрозрачный (как куб головы — гарантированно виден).
            foreach (int f in order)
            {
                // Лёгкое затенение граней (свет сверху) — объём читается.
                double sh = f == 5 ? 1.0 : (f == 4 ? 0.35 : 0.7);
                float r = 0.15f * (float)sh, g = 1.0f * (float)sh, b = 0.3f * (float)sh;
                for (int t = 0; t < 6; t++)
                {
                    var p = scr[faceTris[f][t]];
                    float nx = 2.0f * (p.u / _width) - 1.0f;
                    float ny = 1.0f - 2.0f * (p.v / _height);
                    vb[o++] = nx; vb[o++] = ny; vb[o++] = 0; vb[o++] = 1f;
                    vb[o++] = r; vb[o++] = g; vb[o++] = b; vb[o++] = 1f;
                }
            }

            if (_gcubeVb == null)
                _gcubeVb = _device!.CreateBuffer(new BufferDescription(
                    (uint)(GCubeVerts * 32), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
            var ctx = _context!;
            unsafe
            {
                var mp = ctx.Map(_gcubeVb!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                System.Runtime.InteropServices.Marshal.Copy(vb, 0, mp.DataPointer, vb.Length);
                ctx.Unmap(_gcubeVb!);
            }
            ctx.IASetInputLayout(_cil);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.IASetVertexBuffer(0, _gcubeVb, 32, 0);
            ctx.VSSetShader(_cvsh);
            ctx.PSSetShader(_cpsh!);
            ctx.OMSetBlendState(_tblend);
            ctx.OMSetRenderTargets(_rtv, null);
            ctx.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, _width, _height));
            ctx.Draw(GCubeVerts, 0);
        }

        // ================================================================
        // v40.7: СЕТКА = КРУГ R=100 м ВОКРУГ ГРУЗОВИКА, ШАГ 1 м.
        // ОРАНЖЕВЫЕ тонкие линии (1px) + ярко-КРАСНЫЕ точки в узлах квадратов.
        // Точки — только в «верхних-левых»/чётных узлах (углах клеток 2×2), 2px.
        // ================================================================
        private void DrawPlaneGrid(ArGameState? s)
        {
            if (s == null || _cvsh == null || _cpsh == null || _cil == null) return;
            double planeY = s.GroundY + s.PlaneOffsetM;
            const double RadiusM = 100.0;
            const float HalfW = 0.45f;     // тонкая линия ~1px
            const float BaseA = 0.45f;     // оранжевые линии (на 30% непрозрачнее: 0.35→0.455)
            const float PointA = 0.9f;     // ярко-красные точки
            const int MaxSegs = 100000;

            float[] vb = new float[MaxSegs * 6 * 8];
            int o = 0;

            float VertAlpha(float vPx)
            {
                float center = _height / 2f;
                float below25 = center + _height * 0.25f;
                if (vPx <= below25) return 1f;
                float t = (vPx - below25) / Math.Max(1f, _height - below25);
                return Math.Clamp(1f - t, 0f, 1f);
            }

            float DistFade(double wx, double wz)
            {
                double dx = wx - s.CamX, dz = wz - s.CamZ;
                double d = Math.Sqrt(dx * dx + dz * dz);
                return (float)Math.Clamp(1.0 - d / RadiusM, 0.0, 1.0);
            }

            void EmitSeg(float u1, float v1, float u2, float v2, float a1, float a2, float r, float g, float b)
            {
                if (o + 6 * 8 > vb.Length) return;
                float dx = u2 - u1, dy = v2 - v1;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len < 1e-3f) return;
                float px = -dy / len * HalfW, py = dx / len * HalfW;
                void Emit(float x, float y, float a)
                {
                    vb[o++] = 2f * (x / _width) - 1f;
                    vb[o++] = 1f - 2f * (y / _height);
                    vb[o++] = 0; vb[o++] = 1f;
                    vb[o++] = r * a; vb[o++] = g * a; vb[o++] = b * a; vb[o++] = a;
                }
                float ax = u1 + px, ay = v1 + py, bx = u1 - px, by = v1 - py;
                float cx = u2 - px, cy = v2 - py, dx2 = u2 + px, dy2 = v2 + py;
                Emit(ax, ay, a1); Emit(bx, by, a1); Emit(cx, cy, a2);
                Emit(ax, ay, a1); Emit(cx, cy, a2); Emit(dx2, dy2, a2);
            }

            // Заливка клетки (2 треугольника) заданным цветом/альфой.
            void EmitQuad(float u0, float v0, float u1, float v1, float u2, float v2, float u3, float v3, float a, float r, float g, float b)
            {
                if (o + 6 * 8 > vb.Length) return;
                void Emit(float x, float y)
                {
                    vb[o++] = 2f * (x / _width) - 1f;
                    vb[o++] = 1f - 2f * (y / _height);
                    vb[o++] = 0; vb[o++] = 1f;
                    vb[o++] = r * a; vb[o++] = g * a; vb[o++] = b * a; vb[o++] = a;
                }
                Emit(u0, v0); Emit(u1, v1); Emit(u2, v2);
                Emit(u0, v0); Emit(u2, v2); Emit(u3, v3);
            }

            long camXi = (long)Math.Round(s.CamX);
            long camZi = (long)Math.Round(s.CamZ);

            // 0. ШАХМАТНАЯ заливка (белый, 60% прозрачнее = альфа 0.4).
            // Заливаем клетки 1×1 м, где (x+z) чётное.
            for (long x = camXi - 100; x < camXi + 100; x++)
            {
                for (long z = camZi - 100; z < camZi + 100; z++)
                {
                    if (((x + z) & 1) != 0) continue;   // шахматный порядок
                    var p00 = ProjectPoint(x, planeY, z, s);
                    var p10 = ProjectPoint(x + 1, planeY, z, s);
                    var p11 = ProjectPoint(x + 1, planeY, z + 1, s);
                    var p01 = ProjectPoint(x, planeY, z + 1, s);
                    if (p00 == null || p10 == null || p11 == null || p01 == null) continue;
                    if (!p00.Value.inFront || !p10.Value.inFront || !p11.Value.inFront || !p01.Value.inFront) continue;
                    float a = 0.4f * DistFade(x + 0.5, z + 0.5) * VertAlpha((p00.Value.v + p11.Value.v) * 0.5f);
                    if (a < 0.015f) continue;
                    EmitQuad(p00.Value.u, p00.Value.v, p10.Value.u, p10.Value.v,
                             p11.Value.u, p11.Value.v, p01.Value.u, p01.Value.v, a, 1f, 1f, 1f);
                }
            }

            // 1. ОРАНЖЕВАЯ сетка 1 м (все линии, чёткие и тонкие).
            for (long k = camXi - 100; k <= camXi + 100; k++)
            {
                double dk = Math.Abs(k - s.CamX);
                if (dk >= RadiusM) continue;
                double halfLen = Math.Sqrt(RadiusM * RadiusM - dk * dk);
                for (double z = camZi - halfLen; z < camZi + halfLen - 1e-6; z += 1.0)
                {
                    double z0 = z, z1 = Math.Min(z + 1.0, camZi + halfLen);
                    var p1 = ProjectPoint(k, planeY, z0, s);
                    var p2 = ProjectPoint(k, planeY, z1, s);
                    if (p1 == null || p2 == null || !p1.Value.inFront || !p2.Value.inFront) continue;
                    float f1 = BaseA * DistFade(k, z0) * VertAlpha(p1.Value.v);
                    float f2 = BaseA * DistFade(k, z1) * VertAlpha(p2.Value.v);
                    if (f1 < 0.015f && f2 < 0.015f) continue;
                    EmitSeg(p1.Value.u, p1.Value.v, p2.Value.u, p2.Value.v, f1, f2, 1.0f, 0.62f, 0.0f);
                }
            }
            for (long k = camZi - 100; k <= camZi + 100; k++)
            {
                double dk = Math.Abs(k - s.CamZ);
                if (dk >= RadiusM) continue;
                double halfLen = Math.Sqrt(RadiusM * RadiusM - dk * dk);
                for (double x = camXi - halfLen; x < camXi + halfLen - 1e-6; x += 1.0)
                {
                    double x0 = x, x1 = Math.Min(x + 1.0, camXi + halfLen);
                    var p1 = ProjectPoint(x0, planeY, k, s);
                    var p2 = ProjectPoint(x1, planeY, k, s);
                    if (p1 == null || p2 == null || !p1.Value.inFront || !p2.Value.inFront) continue;
                    float f1 = BaseA * DistFade(x0, k) * VertAlpha(p1.Value.v);
                    float f2 = BaseA * DistFade(x1, k) * VertAlpha(p2.Value.v);
                    if (f1 < 0.015f && f2 < 0.015f) continue;
                    EmitSeg(p1.Value.u, p1.Value.v, p2.Value.u, p2.Value.v, f1, f2, 1.0f, 0.62f, 0.0f);
                }
            }

            // 2. ЯРКО-КРАСНЫЕ точки 4px В КАЖДОМ пересечении линий (каждый целый X и Z).
            for (long x = camXi - 60; x <= camXi + 60; x++)
            {
                for (long z = camZi - 60; z <= camZi + 60; z++)
                {
                    var p = ProjectPoint(x, planeY, z, s);
                    if (p == null || !p.Value.inFront) continue;
                    float a = PointA * DistFade(x, z) * VertAlpha(p.Value.v);
                    if (a < 0.015f) continue;
                    float u = p.Value.u, v = p.Value.v;
                    EmitSeg(u - 2, v, u + 2, v, a, a, 1f, 0f, 0f);
                    EmitSeg(u, v - 2, u, v + 2, a, a, 1f, 0f, 0f);
                }
            }

            if (o <= 0) return;
            if (_gcubeVb == null)
                _gcubeVb = _device!.CreateBuffer(new BufferDescription(
                    (uint)(MaxSegs * 6 * 32), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
            var ctx = _context!;
            unsafe
            {
                var mp = ctx.Map(_gcubeVb!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                System.Runtime.InteropServices.Marshal.Copy(vb, 0, mp.DataPointer, o);
                ctx.Unmap(_gcubeVb!);
            }
            ctx.IASetInputLayout(_cil);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.IASetVertexBuffer(0, _gcubeVb, 32, 0);
            ctx.VSSetShader(_cvsh);
            ctx.PSSetShader(_cpsh!);
            ctx.OMSetBlendState(_tblend);
            ctx.OMSetRenderTargets(_rtv, null);
            ctx.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, _width, _height));
            ctx.Draw((uint)(o / 8), 0);
        }

        // Проецирует мировую точку на плоскость в экранные px (для заливки плоскости).
        private (float u, float v, bool ok) Proj(ArGameState s, double planeY, double x, double z)
        {
            var p = ProjectPoint(x, planeY, z, s);
            if (!p.HasValue || !p.Value.inFront) return (0, 0, false);
            return (p.Value.u, p.Value.v, true);
        }

        // Добавляет отрезок сетки (проецирует обе точки; отбрасывает за спиной).
        // v99: также считает МИРОВУЮ дистанцию середины отрезка до грузовика (fade).
        private void AddGridSeg(List<(float u1, float v1, float u2, float v2, float wdist)> segs,
            ArGameState s, double planeY, double x1, double z1, double x2, double z2)
        {
            var p1 = ProjectPoint(x1, planeY, z1, s);
            var p2 = ProjectPoint(x2, planeY, z2, s);
            if (!p1.HasValue || !p2.HasValue) return;
            if (!p1.Value.inFront || !p2.Value.inFront) return;
            // Мировая дистанция середины отрезка от грузовика (для затухания).
            double mcx = (x1 + x2) / 2 - s.CamX, mcz = (z1 + z2) / 2 - s.CamZ;
            double wdist = Math.Sqrt(mcx * mcx + mcz * mcz);
            segs.Add((p1.Value.u, p1.Value.v, p2.Value.u, p2.Value.v, (float)wdist));
        }

        // ================================================================
        // v96: МЯГКОЕ ПЯТНО (псевдо-тень) под меткой новой точки на плоскости.
        // Если метка (pin) стоит НЕ на плоскости (Y ≠ planeY) — рисуем овальное
        // пятно на плоскости в XZ-координатах точки (100% лежит на плоскости).
        // Если метка ровно на плоскости (Y == planeY) — тень не нужна.
        // ================================================================
        private void DrawPinShadow(ArGameState? s)
        {
            if (s?.Pin == null) return;
            var pin = s.Pin.Value;
            double planeY = s.GroundY + s.PlaneOffsetM;
            // Если метка ровно на плоскости — тень не нужна.
            if (Math.Abs(pin.Y - planeY) < 0.001) return;

            // Проецируем точку на плоскости (XZ метки, Y = planeY).
            var pp = ProjectPoint(pin.X, planeY, pin.Z, s);
            if (!pp.HasValue || !pp.Value.inFront) return;

            // Размер пятна: зависит от высоты метки над плоскостью (чем выше — тем больше).
            double h = Math.Abs(pin.Y - planeY);
            float rx = (float)(18 + h * 6);
            float ry = rx * 0.45f;   // овал, сплюснутый по вертикали (вид сверху-сбоку)
            // Прозрачность: чем выше — тем прозрачнее (мягче).
            float a = (float)Math.Clamp(0.5 - h * 0.02, 0.12, 0.5);
            DrawEllipse(pp.Value.u, pp.Value.v, rx, ry, 0f, 0f, 0f, a);
        }

        // Высота отображения цели: КОПИЯ displayYFor ar_hud.js (v70/v72):
        // Y≠0 — как есть (+0.5 groundOffset); Y=0 — от ближайшего города,
        // <350 м плавный переход к высоте фуры, НЧ-фильтр 0.08, захват <50 м.
        private double _dispY; private bool _hasDispY;
        private double TargetDisplayY(ArGameState s, ArMarker t, double dist2d)
        {
            if (Math.Abs(t.Y) > 0.001) return t.Y + 0.5;
            double cityY = s.CamY; double bd = double.MaxValue; bool any = false;
            foreach (var c in s.Cities)
            {
                double d2 = (c.X - t.X) * (c.X - t.X) + (c.Z - t.Z) * (c.Z - t.Z);
                if (d2 < bd) { bd = d2; cityY = c.Y; any = true; }
            }
            const double hCity = 350.0, hLock = 50.0;
            double targetY;
            if (dist2d >= hCity) targetY = cityY;
            else
            {
                double k = Math.Clamp(1 - (dist2d - hLock) / (hCity - hLock), 0.0, 1.0);
                targetY = cityY + (s.CamY - cityY) * k;
            }
            double y = _hasDispY ? _dispY + (targetY - _dispY) * 0.08 : targetY;
            if (dist2d < hLock) y = _hasDispY ? _dispY + (s.CamY - _dispY) * 0.08 : targetY;
            _hasDispY = true; _dispY = y;
            return y;
        }

        // ================================================================
        // v86: PINHOLE-ПРОЕКЦИЯ (CabinArProjection — копилка FOV, §4/5/8/9):
        // FOV=65° конфиг, вертикальный FOV из aspect, ProjectionCenter.
        // Переключатель UsePinholeProjection (конфиг) — A/B-сравнение со
        // старым путём v85 для диагностики «плавания» при повороте головы.
        // ================================================================
        public static bool UsePinholeProjection = true;   // v86: A/B-переключатель
        // v92: FOV берётся из dumb-приёмника ArBridge.FovDegrees (меняется
        // приложением через Ctrl+колесо). Статик CabinFovDegrees оставлен как
        // стартовое значение, но рендер читает ArBridge каждый кадр.
        public static double CabinFovDegrees = 100.0;      // v40.1: временная калибровка пользователя (было 95)
        private readonly CabinArProjection _pinhole = new();

        // ================================================================
        // ПРОЕКЦИЯ (v85): ТОЧНАЯ КОПИЯ projectPoint из ar_hud.js (AR v1,
        // подтверждена пользователем «практически идеально»):
        //   yaw = YawBase*2π + YawHead*2π (YawHead здесь — ДОЛЯ ОБОРОТА, как в JS);
        //   fwd = (-sin yaw, -cos yaw), right = (cos yaw, -sin yaw) — знаки миникарты;
        //   НИКАКИХ зеркал и флипов (ArFlipHorizontal/ArFlipZ УДАЛЕНЫ) — v83-ручки
        //   компенсировали симптом, но не причину: вертикальная метка «с ускорением»
        //   при повороте головы. Разница v83→v85: в v83-YawHead применялся как РАДИАНЫ
        //   (двойное умножение на 2π) и зеркала ломали знак целиком.
        //   КОМПОЗИТНЫЙ ПИТЧ (v75 JS): 1) кузов вращает луч вокруг right,
        //   2) голова добавляется сверху (тот же приём).
        // v86: при UsePinholeProjection=true — маршрут через CabinArProjection
        // (pinhole, FOV 65° конфиг, projection center); иначе — старый путь v85.
        // ================================================================
        private (float u, float v, bool inFront, double dist, double depth)? ProjectPoint(
            double wx, double wy, double wz, ArGameState? s)
        {
            if (s == null) return null;
            if (UsePinholeProjection)
            {
                // PINHOLE-путь (v86): общая геометрия, конфиг FOV/центра.
                var r = _pinhole.Project(wx, wy, wz,
                    s.CamX, s.CamY, s.CamZ,
                    s.YawBase, s.PitchBody, s.YawHead, s.PitchHead,
                    _width, _height);
                _pinhole.CabinFovDegrees = ArBridge.FovDegrees;   // v92: из dumb-приёмника
                bool inFront = r.depth > 0.5;
                double dist3 = Math.Sqrt((wx - s.CamX) * (wx - s.CamX) +
                                         (wy - s.CamY) * (wy - s.CamY) +
                                         (wz - s.CamZ) * (wz - s.CamZ));
                return (r.u, r.v, inFront, dist3, r.depth);
            }
            // YawHead в ArGameState — ДОЛЯ ОБОРОТА (как head.offset в TruckTel),
            // поэтому ×2π — как в JS (c.yawHead уже рад, тут приводим к тому же).
            double yaw = s.YawBase * Math.PI * 2 + s.YawHead * Math.PI * 2;
            const double eyeH = 1.5;   // v40.7: Actros — глаза 2.25 м от полотна − 0.75 (опорная точка)
            double ex = s.CamX, ey = s.CamY + eyeH, ez = s.CamZ;

            double rx = wx - ex, rz = wz - ez;
            double ry = wy - ey;

            double s1 = Math.Sin(yaw), c1 = Math.Cos(yaw);
            double fwdX = -s1, fwdZ = -c1;
            double rightX = c1, rightZ = -s1;

            double fdot0 = rx * fwdX + rz * fwdZ;
            double rdot = rx * rightX + rz * rightZ;

            // 1) ПИТЧ КУЗОВА — поворот луча вокруг right (как в JS v75):
            double bodyPitch = s.PitchBody * Math.PI * 2;
            double cosB = Math.Cos(bodyPitch), sinB = Math.Sin(bodyPitch);
            double fwd1 = fdot0 * cosB + ry * sinB;
            double up1 = ry * cosB - fdot0 * sinB;

            // 2) ПИТЧ ГОЛОВЫ — добавляется к кузову (та же ось right):
            double headPitch = s.PitchHead * Math.PI * 2;
            double cosH = Math.Cos(headPitch), sinH = Math.Sin(headPitch);
            double depth = fwd1 * cosH + up1 * sinH;
            double up = up1 * cosH - fwd1 * sinH;

            if (depth <= 0.5) return (0, 0, false, 0, depth);   // за спиной/слишком близко

            double fovTan = Math.Tan(75.0 * Math.PI / 180 / 2);
            double f = _width * 0.5 / fovTan;
            double u = _width / 2.0 + f * (rdot / depth);
            double v = _height / 2.0 - f * (up / depth);
            if (!double.IsFinite(u) || !double.IsFinite(v)) return (0, 0, false, 0, depth);
            double dist = Math.Sqrt(rx * rx + ry * ry + rz * rz);
            return ((float)u, (float)v, true, dist, depth);
        }

        // Сглаживание экранных позиций (как CFG.smooth/ar_hud.js): метка без рывков
        // между пакетами телеметрии 30 Гц; при смене цели — мгновенный прыжок.
        private sealed class SmoothState { public float U, V; public string Ident = ""; }
        private SmoothState? _smTarget;
        private SmoothState? _smPin;
        private const float SmoothK = 0.25f;   // коэффициент лерпа на кадр (JS CFG.smooth)

        private static float LerpTo(float cur, float target)
            => cur + (target - cur) * SmoothK;

        // ================================================================
        // v95: ДИСТАНЦИЯ до земли по ПРИЦЕЛЬНОЙ точке (центр экрана).
        // Луч взгляда: глаза (camY + eyeH), направление по yaw + композитный
        // питч (кузов → голова). Пересечение с плоскостью Y = groundY
        // (где «стоят колёса»). Возвращает горизонтальную(3D) дистанцию, м.
        // ================================================================
        private static double ComputeGroundDistance(ArGameState? s)
        {
            if (s == null) return double.NaN;
            const double eyeH = 1.5;   // v40.7: Actros — глаза 2.25 м от полотна − 0.75 (опорная точка)
            double eyeY = s.CamY + eyeH;
            // v99: плоскость земли = GroundY + PlaneOffsetM (влияет на дистанцию
            // под прицельной точкой — та же плоскость, куда ставится метка).
            double groundY = s.GroundY + s.PlaneOffsetM;
            double dy = groundY - eyeY;                 // глаза → земля
            if (dy >= 0) return double.NaN;             // камера ниже земли — нет цели

            // Направление луча (как в projectPoint, но единичное, из центра экрана).
            double yaw = s.YawBase * Math.PI * 2 + s.YawHead * Math.PI * 2;
            double fwdX = -Math.Sin(yaw), fwdZ = -Math.Cos(yaw);

            // Луч из центра экрана = направление взгляда.
            // fwd (горизонт) и up строятся через композитный питч (кузов → голова).
            double bp = s.PitchBody * Math.PI * 2;
            double hp = s.PitchHead * Math.PI * 2;
            double pitchTotal = bp + hp;
            double fwdLen = Math.Cos(pitchTotal);       // горизонтальная часть
            double dirY = Math.Sin(pitchTotal);          // вертикальная часть (+вверх)
            double dirX = fwdX * fwdLen;
            double dirZ = fwdZ * fwdLen;
            if (dirY >= -1e-6) return double.NaN;        // смотрим выше горизонта

            double t = dy / dirY;                        // t>0
            if (t <= 0) return double.NaN;
            double gx = s.CamX + dirX * t;
            double gz = s.CamZ + dirZ * t;
            // Горизонтальная дистанция от грузовика до точки на земле.
            return Math.Sqrt((gx - s.CamX) * (gx - s.CamX) + (gz - s.CamZ) * (gz - s.CamZ));
        }

        /// <summary>История проекции цели для отрисовки.</summary>
        public readonly record struct MarkerProjection(float U, float V, bool Visible, double Dist);

        public void WaitForNextFrame()
        {
            // v100: composition swap chain (flip) — каденс задаёт vsync Present(1)
            // внутри RenderFrame; этот метод no-op (оставлен для API).
        }

        /// <summary>ОДИН кадр. Всё между «взять latest» и Present — лёгкое (архитектура).</summary>
        public void RenderFrame()
        {
            if (_device == null || _context == null || _rtv == null || _swapChain == null) return;

            // 1) latest GameState (без блокировок, без очереди устаревших).
            var state = ArBridge.Game.Latest;

            var ctx = _context;
            // v100: полностью прозрачный clear (RGB=0, A=0) — не COLORKEY.
            ctx.ClearRenderTargetView(_rtv, new Color4(0f, 0f, 0f, 0f));

            // ============================================================
            // v40.4 ПОРЯДОК СЛОЁВ: сетка ПЛОСКОСТИ — САМЫЙ ДАЛЬНИЙ СЛОЙ,
            // рисуется ПЕРВОЙ (сразу после очистки). Всё остальное
            // (прицел, куб, pin, маркер, текст) — поверх, обводка/тени текста
            // больше не искажаются сеткой.
            // ============================================================
            try
            {
                if (state != null && state.ShowGrid) DrawPlaneGrid(state);
            }
            catch { /* сетка не должна ломать рендер */ }

            // ============================================================
            // v95: ПРИЦЕЛЬНЫЙ МИКРО-КРЕСТИК по центру экрана — 5 пикселей
            // (центр + вверх/вниз/влево/вправо), белый 0.9. Всегда.
            // v96: под крестиком — ПОЛУПРОЗРАЧНАЯ МЯГКАЯ ТЕНЬ (тёмный квадрат
            // с размытыми краями), чтобы крестик читался и на светлом фоне.
            // ============================================================
            float ccx = _width / 2f, ccy = _height / 2f;
            // Тень: тёмный полупрозрачный квадрат 9×9 под крестиком (смещён на 1px вниз-вправо).
            DrawBox(ccx - 4.5f + 1f, ccy - 4.5f + 1f, 9f, 9f, 0f, 0f, 0f, 0.35f);
            DrawBox(ccx - 0.5f, ccy - 0.5f, 1f, 1f, 1f, 1f, 1f, 0.9f);   // центр
            DrawBox(ccx - 0.5f, ccy - 2.5f, 1f, 1f, 1f, 1f, 1f, 0.65f);  // вверх
            DrawBox(ccx - 0.5f, ccy + 1.5f, 1f, 1f, 1f, 1f, 1f, 0.65f);  // вниз
            DrawBox(ccx - 2.5f, ccy - 0.5f, 1f, 1f, 1f, 1f, 1f, 0.65f);  // влево
            DrawBox(ccx + 1.5f, ccy - 0.5f, 1f, 1f, 1f, 1f, 1f, 0.65f);  // вправо

            float m = 70f;   // edgeMargin (CFG / ar_hud.js)

            // ============================================================
            // v95: 3D-КУБ (ориентация головы) в правом нижнем углу.
            // ============================================================
            try { DrawHeadCube(state); } catch { /* не должно ломать рендер */ }

            // ============================================================
            // v40.4: тень пина — после сетки, перед текстами.
            // ============================================================
            try { DrawPinShadow(state); } catch { }

            // ============================================================
            // v95: ДИСТАНЦИЯ до земли по ПРИЦЕЛЬНОЙ точке (центр экрана).
            // Луч взгляда (глаза + yaw/композитный питч) ∩ плоскость Y=groundY
            // (где «стоят колёса»). Отрисовка текста ниже микрокрестика.
            // + ЗАПИСЬ ДАННЫХ (головной питч + дистанция) в файл для калибровки
            // конуса обзора (миникарта/редактор) — v95.
            // ============================================================
            try
            {
                using var gFont = new Font("Consolas", 8.5f, FontStyle.Bold);   // v97: шрифт как у FOV (8.5f)
                string gText = "—";
                double gd2 = ComputeGroundDistance(state);
                if (double.IsFinite(gd2) && gd2 > 0) gText = FmtDist(gd2);
                EnsureText(ref _gndTxt, gText, gFont, System.Drawing.Color.White, 2f);
                if (_gndTxt != null)
                    // v97: текст дистанции ПОД крестиком (отступ 2px), шрифт как у FOV.
                    DrawTextSprite(_gndTxt, _width / 2f - _gndTxt.Width / 2f, _height / 2f + 2f);
            }
            catch { /* не должно ломать рендер */ }

            // ============================================================
            // v85: ПОМЕТКА (PIN) — DUMB-ПРИЁМНИК (v73 JS): ArBridge.Pin ставится
            // по ar_pin (+X / кнопка «Пометить в АР»); проекция и отрисовка —
            // на рендере серым крестиком «Новая точка», НЕЗАВИСИМО от цели.
            // ============================================================
            if (state?.Pin != null)
            {
                var pin = state.Pin.Value;
                var pp = ProjectPoint(pin.X, pin.Y, pin.Z, state);
                if (pp.HasValue && pp.Value.inFront)
                {
                    float pu = Math.Clamp(pp.Value.u, m, _width - m);
                    float pv = Math.Clamp(pp.Value.v, m, _height - m);
                    if (_smPin == null) _smPin = new SmoothState { U = pu, V = pv };
                    else { _smPin.U = LerpTo(_smPin.U, pu); _smPin.V = LerpTo(_smPin.V, pv); }

                    // Серый круг + крест (как pin-отрисовка ar_hud.js v70/v73).
                    DrawCircle(_smPin.U, _smPin.V, 6f, 0.63f, 0.63f, 0.63f, 0.95f);
                    DrawBox(_smPin.U - 13f, _smPin.V - 1.0f, 26f, 2.0f, 0.88f, 0.88f, 0.88f, 0.95f);
                    DrawBox(_smPin.U - 1.0f, _smPin.V - 13f, 2.0f, 26f, 0.88f, 0.88f, 0.88f, 0.95f);

                    try
                    {
                        using var pinFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
                        EnsureText(ref _pinTxt, "Новая точка  \u00B7  " + FmtDist(pp.Value.dist), pinFont,
                            System.Drawing.Color.FromArgb(220, 220, 220));
                        DrawTextSprite(_pinTxt, _smPin.U - _pinTxt!.Width / 2f, _smPin.V + 18f);
                    }
                    catch { /* текст не должен ломать рендер */ }
                }
            }
            else _smPin = null;

            // ============================================================
            // МАРКЕР ЦЕЛИ (v85: копия отрисовки цели ar_hud.js): цвет категории,
            // размер 15..33 px по дистанции, затухание 500→1500 м, сглаживание,
            // подписи realName · дист + системное имя (v85 — требование).
            // ============================================================
            if (state?.Target != null)
            {
                var tg = state.Target;
                double dist2d = Math.Sqrt((tg.X - state.CamX) * (tg.X - state.CamX) +
                                          (tg.Z - state.CamZ) * (tg.Z - state.CamZ));
                double wy = TargetDisplayY(state, tg, dist2d);
                var pr = ProjectPoint(tg.X, wy, tg.Z, state);

                if (pr.HasValue && pr.Value.inFront && pr.Value.depth > 0.5)
                {
                    float u = Math.Clamp((float)pr.Value.u, m, _width - m);
                    float v = Math.Clamp((float)pr.Value.v, m, _height - m);
                    string ident = tg.GameName + "|" + tg.X.ToString("F1") + "," + tg.Z.ToString("F1");
                    if (_smTarget == null || _smTarget.Ident != ident)
                        _smTarget = new SmoothState { U = u, V = v, Ident = ident };
                    else { _smTarget.U = LerpTo(_smTarget.U, u); _smTarget.V = LerpTo(_smTarget.V, v); }

                    // Размер/прозрачность по дистанции (sizeAlphaFor JS).
                    // v95: точка в ДВА раза меньше (радиус = size/4 вместо size/2).
                    double d = Math.Max(0, pr.Value.dist);
                    float size = d <= 10 ? 33f : d >= 500 ? 15f :
                        (float)(33 + (15 - 33) * (d - 10) / (500 - 10));
                    float alpha = d <= 500 ? 1f :
                        Math.Clamp(1f - (float)((d - 500) / (1500 - 500)), 0.12f, 1f);

                    var (mr, mg, mb) = ColorFor(tg);
                    DrawCircle(_smTarget.U, _smTarget.V, size / 4f, mr, mg, mb, alpha);

                    // Подписи (v70 JS): строка 1 = realName · дистанция; строка 2 (v85)=
                    // СИСТЕМНОЕ ИМЯ мелким шрифтом (требование), если отличается.
                    if (alpha > 0.03f)
                    {
                        try
                        {
                            using var nameFont = new Font("Segoe UI", 12.5f, FontStyle.Bold);
                            using var gameFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
                            EnsureText(ref _txt1,
                                (string.IsNullOrEmpty(tg.RealName) ? tg.GameName : tg.RealName) +
                                "  \u00B7  " + FmtDist(pr.Value.dist),
                                nameFont, System.Drawing.Color.White);
                            float tu = _smTarget.U - _txt1!.Width / 2f;
                            float tv = _smTarget.V + size / 4f + _txt1.Height + 6f;  // v95: 1 пустая строка ниже точки
                            DrawTextSprite(_txt1, tu, tv, alpha);
                            if (!string.IsNullOrEmpty(tg.GameName) && tg.GameName != tg.RealName)
                            {
                                EnsureText(ref _txt2, tg.GameName, gameFont,
                                    System.Drawing.Color.FromArgb(217, 255, 255, 255));
                                if (_txt2 != null)
                                    DrawTextSprite(_txt2, _smTarget.U - _txt2.Width / 2f,
                                        tv + _txt1.Height - 14f, alpha);
                            }
                        }
                        catch { /* текст не должен ломать рендер */ }
                    }
                }
            }
            else _smTarget = null;

            // ============================================================
            // v92/v93: ИНДИКАТОР FOV в левом нижнем углу (Ctrl+Shift+PGUP/PGDN).
            // v95: текст «AR Overlay FOV: NN.N°» с обводкой 3px (по умолчанию).
            // v96: шрифт в 2 раза меньше (6.5f), ниже к краю (отступ на строку),
            // под текстом — ПОЛУПРОЗРАЧНАЯ ПЛАШКА; в скобках — высота плоскости.
            // ============================================================
            try
            {
                using var fovFont = new Font("Consolas", 8.5f, FontStyle.Bold);   // v97: +30% (было 6.5f)
                string fovText = "AR Overlay FOV: " + ArBridge.FovDegrees.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "°";
                // v96: высота плоскости земли в скобках рядом с FOV.
                if (Math.Abs(ArBridge.PlaneOffsetM) > 0.001)
                    fovText += "  (" + ArBridge.PlaneOffsetM.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " м)";
                EnsureText(ref _fovTxt, fovText, fovFont, System.Drawing.Color.White, 3f);
                if (_fovTxt != null)
                {
                    float fovX = 12f;
                    float fovY = _height - _fovTxt.Height - 4f;   // v96: ниже к краю (отступ 4px)
                    // Полупрозрачная плашка под текстом (тёмная, скруглённая).
                    DrawBox(fovX - 4f, fovY - 2f, _fovTxt.Width + 8f, _fovTxt.Height + 4f, 0f, 0f, 0f, 0.35f);
                    DrawTextSprite(_fovTxt, fovX, fovY);
                }
            }
            catch { /* индикатор не должен ломать рендер */ }

            _swapChain!.Present(1, 0u);   // v100: composition swap chain — vsync каденс (flip model)
        }

        public void Resize(int width, int height)
        {
            _width = width; _height = height;
            _rtv?.Dispose();
            _swapChain!.ResizeBuffers(0, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
            using (ID3D11Texture2D back = _swapChain.GetBuffer<ID3D11Texture2D>(0))
                _rtv = _device!.CreateRenderTargetView(back);
            // v100: после ResizeBuffers обновить content visual (DComp).
            if (_dcompVisual != null && _swapChain != null)
            {
                DirectComposition.SetContent(_dcompVisual, _swapChain.NativePointer);
                if (_dcompDevice != null) DirectComposition.Commit(_dcompDevice);
            }
        }

        // ==================== РАМКА ОТСЕЧЕНИЯ + ПРЕДИКЦИЯ НЕ В AR2 (v83 не нужно — рендер идёт на vsync GPU) ====================
        // (Рамка отсечения есть только в AR v1 JS — пользователь просил «визуализируй
        //  границы отсечения» для JS-версии; в нативной версии границы те же, но
        //  рисовать их на GPU-кваде без смысла — маркер уже прижимается сам.)

        // ==================== ТЕКСТОВЫЙ КОНВЕЙЕР (v83) ====================
        // Пререндер строки в BGRA-текстуру через GDI (System.Drawing) + отрисовка
        // квада шейдером текста (сэмпл + discard по альфе). Текст меняется редко,
        // пререндер только при изменении содержимого (кэш TextSprite.Text).
        private const string TextHlsl = """
            struct VS_IN { float2 pos : POS; };
            struct VS_OUT { float4 pos : SV_POSITION; float2 uv : UV; };
            cbuffer Screen : register(b0) { float2 Viewport; float2 _pad; };
            cbuffer Rect  : register(b1) { float4 R; /* xy=top-left px, zw=size px */ float4 TintW; /* w=alpha */ };
            VS_OUT TMain(VS_IN i) {
                VS_OUT o;
                float2 px = R.xy + i.pos * R.zw;
                o.pos = float4(px.x / Viewport.x * 2 - 1, 1 - px.y / Viewport.y * 2, 0, 1);
                o.uv = i.pos;
                return o;
            }
            Texture2D    Txt   : register(t0);
            SamplerState Smpl  : register(s0);
            float4 TMainP(VS_OUT i) : SV_TARGET {
                float4 c = Txt.Sample(Smpl, i.uv);   // BGRA: текст + обводка + альфа (premultiplied)
                c.w *= TintW.w;                      // v85: множитель альфы (затухание по дистанции)
                // v100: premultiplied — RGB уже умножены на alpha в спрайте.
                return c;
            }
            """;
        private ID3D11VertexShader? _tvs;
        private ID3D11PixelShader? _tps;
        private ID3D11InputLayout? _til;
        private ID3D11Buffer? _tvb, _tcbRect;
        private ID3D11SamplerState? _tsampler;
        private ID3D11BlendState? _tblend;

        private sealed class TextSprite : IDisposable
        {
            public ID3D11Texture2D? Tex;
            public ID3D11ShaderResourceView? Srv;
            public float Width, Height;
            public string Text = "";
            public void Dispose() { Srv?.Dispose(); Tex?.Dispose(); }
        }
        private TextSprite? _txt1, _txt2, _pinTxt;   // 1 = «имя · дистанция», 2 = системное имя (v85), pin = «Новая точка»
        private TextSprite? _fovTxt;                 // v92: индикатор FOV (левый нижний угол)
        private TextSprite? _gndTxt;                 // v95: дистанция до земли по прицел.

        private void CreateTextPipeline()
        {
            var vsRes = Compiler.Compile(TextHlsl, "TMain", "text", "vs_5_0", out var vsBlob, out var vsErr);
            if (vsRes.Failure || vsBlob == null)
                throw new InvalidOperationException("Text VS compile: " + vsRes.ToString());
            var psRes = Compiler.Compile(TextHlsl, "TMainP", "text", "ps_5_0", out var psBlob, out var psErr);
            if (psRes.Failure || psBlob == null)
                throw new InvalidOperationException("Text PS compile: " + psRes.ToString());

            _tvs = _device!.CreateVertexShader(vsBlob, null);
            _tps = _device.CreatePixelShader(psBlob, null);
            _til = _device.CreateInputLayout(new[]
            {
                new InputElementDescription("POS", 0, Format.R32G32_Float, 0, 0)
            }, vsBlob);

            var verts = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1),
                new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1),
            };
            _tvb = _device.CreateBuffer<Vector2>(verts, BindFlags.VertexBuffer,
                ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None);
            _tcbRect = _device.CreateBuffer(new BufferDescription(
                32, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));

            _tsampler = _device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp
            });

            var bd = new BlendDescription
            {
                AlphaToCoverageEnable = false,
                IndependentBlendEnable = false
            };
            bd.RenderTarget[0] = new RenderTargetBlendDescription
            {
                BlendEnable = true,
                // v100: premultiplied alpha (текст-спрайт уже premultiplied BGRA).
                SourceBlend = Blend.One,
                DestinationBlend = Blend.InverseSourceAlpha,
                BlendOperation = BlendOperation.Add,
                SourceBlendAlpha = Blend.One,
                DestinationBlendAlpha = Blend.InverseSourceAlpha,
                BlendOperationAlpha = BlendOperation.Add,
                RenderTargetWriteMask = ColorWriteEnable.All
            };
            _tblend = _device.CreateBlendState(bd);
        }

        // GDI-пререндер строки в BGRA-спрайт.
        // v87 КАЧЕСТВО: SSAA ×3 — рендер в 3× разрешении + даунскейл
        // HighQualityBicubic → гладкие края без «рваной» обводки/пикселей.
        // Обводка мягкая: 12-направления (шаг 30°) с гауссовым затуханием
        // радиуса + один слой размытия (DrawImage с альфой) — как blur.
        private const int TextSSAA = 3;   // суперсэмплинг текста (2..4; 1 = выкл)

        private TextSprite MakeTextSprite(string text, Font font, System.Drawing.Color fill, float outline = 3f)
        {
            // 1) Размер при целевом разрешении.
            int w, h;
            using (var mg = Graphics.FromHwnd(IntPtr.Zero))
            {
                var sz = mg.MeasureString(text, font);
                w = (int)Math.Ceiling(sz.Width) + 12;
                h = (int)Math.Ceiling(sz.Height) + 12;
            }
            int ss = Math.Max(1, TextSSAA);
            int W = w * ss, H = h * ss;
            const int pad = 6;               // внутренний отступ (в финальных px)
            int padS = pad * ss;

            using var hi = new Bitmap(Math.Max(2, W), Math.Max(2, H), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(hi))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                // v96: МЯГКАЯ ТЕНЬ (drop shadow) — размытый чёрный силуэт текста,
                // смещённый вниз-вправо. Даёт читаемость на СВЕТЛОМ фоне (обводка
                // тонкая, тень — мягкая). Рисуем ДО обводки/заливки.
                // Мягкость: 3 прохода FillPath с растущим радиусом и убывающей альфой
                // (имитация blur) + смещение (2px в ss-масштабе).
                for (int sh = 3; sh >= 1; sh--)
                {
                    using var shadowPath = new System.Drawing.Drawing2D.GraphicsPath();
                    shadowPath.AddString(text, font.FontFamily, (int)font.Style, font.Size * ss,
                        new PointF(padS + 2 * ss + sh, padS + 2 * ss + sh), StringFormat.GenericTypographic);
                    int shA = 30 + sh * 20;   // внешние слои прозрачнее
                    using var shadowBrush = new SolidBrush(System.Drawing.Color.FromArgb(shA, 0, 0, 0));
                    g.FillPath(shadowBrush, shadowPath);
                }

                // МЯГКАЯ ОБВОДКА (v87): штрих с затуханием альфы по радиусу
                // (имитация Gaussian blur обводки как в drawOutlinedText JS).
                // v95: толщина = outline (в целевых px) × 2 для чёткости.
                using var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddString(text, font.FontFamily, (int)font.Style, font.Size * ss,
                    new PointF(padS, padS), StringFormat.GenericTypographic);
                float outlinePx = outline * ss;   // толщина обводки в ss-пикселях
                for (int ring = 3; ring >= 1; ring--)
                {
                    int a = 55 + ring * 30;      // внешние кольца прозрачнее
                    float width = (outlinePx * 0.45f) / (4 - ring);
                    using var pen = new Pen(System.Drawing.Color.FromArgb(
                        Math.Min(255, a * 2), 0, 0, 0), Math.Max(1.2f, width));
                    pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    g.DrawPath(pen, path);
                }
                // Заливка текста — v93 ФИКС: рисовать в ТОМ ЖЕ 3× масштабе, что и
                // обводка (раньше DrawString с оригинальным шрифтом 1× → после
                // даунскейла заливка ~4px, «шрифт уменьшился до нечитаемого»).
                using (var scaledFont = new Font(font.FontFamily, font.Size * ss, font.Style))
                using (var br = new SolidBrush(fill))
                {
                    g.DrawString(text, scaledFont, br, padS, padS);
                }
            }

            // 2) Даунсэмпл ×ss → финальный спрайт (сглаживание + мягкость).
            using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g2 = Graphics.FromImage(bmp))
            {
                g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g2.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g2.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g2.DrawImage(hi, new Rectangle(0, 0, w, h),
                    new Rectangle(0, 0, W, H), GraphicsUnit.Pixel);
            }

            var rect = new System.Drawing.Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                // v100: premultiply BGRA-пиксели (GDI даёт straight alpha, а blend
                // Src=ONE требует premultiplied). Текст меняется редко (кэш) — ок.
                unsafe
                {
                    byte* row = (byte*)data.Scan0;
                    for (int y = 0; y < h; y++)
                    {
                        byte* p = row + y * data.Stride;
                        for (int x = 0; x < w; x++)
                        {
                            byte a = p[3];
                            if (a == 0) { p[0] = 0; p[1] = 0; p[2] = 0; }
                            else if (a < 255)
                            {
                                p[0] = (byte)(p[0] * a / 255);
                                p[1] = (byte)(p[1] * a / 255);
                                p[2] = (byte)(p[2] * a / 255);
                            }
                            p += 4;
                        }
                    }
                }
                var texDesc = new Texture2DDescription
                {
                    Width = (uint)w,
                    Height = (uint)h,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Immutable,
                    BindFlags = BindFlags.ShaderResource,
                    CPUAccessFlags = CpuAccessFlags.None
                };
                // Vortice 3.8.3: initial data для CreateTexture2D — SubresourceData(pData, pitch).
                var sub = new SubresourceData(data.Scan0, (uint)data.Stride);
                var tex = _device!.CreateTexture2D(texDesc, new[] { sub });
                var srv = _device.CreateShaderResourceView(tex);
                return new TextSprite { Tex = tex, Srv = srv, Width = w, Height = h, Text = text };
            }
            finally { bmp.UnlockBits(data); }
        }

        // Кэш по контенту: пересоздаём спрайт ТОЛЬКО когда строка/шрифт сменилась.
        // v95: outline — толщина чёрной обводки в px.
        private void EnsureText(ref TextSprite? sprite, string text, Font font, System.Drawing.Color fill, float outline = 3f)
        {
            if (sprite != null && sprite.Text == text) return;
            try
            {
                sprite?.Dispose();
                sprite = MakeTextSprite(text, font, fill, outline);
            }
            catch { sprite = null; }
        }

        private void DrawTextSprite(TextSprite? sp, float u, float v, float alpha = 1f)
        {
            if (_tvs == null || _tps == null || _til == null || _tvb == null || _tcbRect == null ||
                _tblend == null || _tsampler == null || sp == null || sp.Srv == null) return;
            var ctx = _context!;
            unsafe
            {
                var mp = ctx.Map(_tcbRect!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                var p = (float*)mp.DataPointer;
                p[0] = u; p[1] = v; p[2] = sp.Width; p[3] = sp.Height;
                p[4] = 0f; p[5] = 0f; p[6] = 0f; p[7] = alpha;   // TintW.w = альфа (v85)
                ctx.Unmap(_tcbRect!);
            }
            ctx.IASetInputLayout(_til);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.IASetVertexBuffer(0, _tvb, 8, 0);
            ctx.VSSetShader(_tvs);
            ctx.VSSetConstantBuffer(0, _cbScreen);
            ctx.VSSetConstantBuffer(1, _tcbRect);
            ctx.PSSetShader(_tps!);
            ctx.PSSetConstantBuffer(0, _cbScreen);
            ctx.PSSetConstantBuffer(1, _tcbRect);
            ctx.PSSetShaderResource(0, sp.Srv);
            ctx.PSSetSampler(0, _tsampler);
            ctx.OMSetBlendState(_tblend);
            ctx.OMSetRenderTargets(_rtv, null);
            ctx.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, _width, _height));
            ctx.Draw(6, 0);
        }

        public void Dispose()
        {
            _txt1?.Dispose(); _txt2?.Dispose(); _pinTxt?.Dispose(); _fovTxt?.Dispose();
            _tsampler?.Dispose();
            _tblend?.Dispose();
            _tcbRect?.Dispose();
            _tvb?.Dispose();
            _til?.Dispose();
            _tps?.Dispose();
            _tvs?.Dispose();
            _bcb?.Dispose();
            _bvb?.Dispose();
            _bil?.Dispose();
            _bps?.Dispose();
            _bvs?.Dispose();
            _cvb?.Dispose();
            _cil?.Dispose();
            _cpsh?.Dispose();
            _cvsh?.Dispose();
            _lvb?.Dispose();
            _lil?.Dispose();
            _lps?.Dispose();
            _lvs?.Dispose();
            _ecb?.Dispose();
            _evb?.Dispose();
            _eil?.Dispose();
            _eps?.Dispose();
            _evs?.Dispose();
            _gndTxt?.Dispose();
            _rtv?.Dispose();
            _vb?.Dispose();
            _vs?.Dispose();
            _ps?.Dispose();
            // v100: освободить DirectComposition ДО swap chain/device (use-after-free).
            ReleaseCom(_dcompVisual);
            ReleaseCom(_dcompTarget);
            ReleaseCom(_dcompDevice);
            _dcompVisual = _dcompTarget = _dcompDevice = null;
            _swapChain?.Dispose();
            _device?.Dispose();
            _context?.Dispose();
            _factory?.Dispose();
        }

        // Освобождение COM-объекта DirectComposition (IDCompositionDevice/Visual/Target).
        private static void ReleaseCom(object? obj)
        {
            if (obj == null) return;
            try
            {
                if (obj is IDisposable d) d.Dispose();
                else Marshal.ReleaseComObject(obj);
            }
            catch { /* ignore */ }
        }
    }
}