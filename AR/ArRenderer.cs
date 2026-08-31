using System;
using System.Drawing;
using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Compiler = Vortice.D3DCompiler.Compiler;
using Vortice.Mathematics;

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

            // Требования bitblt-модели для COLORKEY-окна (v80):
            // SwapEffect.Sequential + Scaling.Stretch + Flags.None.
            // DX: Scaling.None валиден ТОЛЬКО для flip-свопчейн (на Windows 10
            // с bitblt даёт DXGI_ERROR_INVALID_CALL — найдено логами 18:14 v79).
            // Флаг waitable тоже только flip. Каденс задаёт vsync Present(1).
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
                SwapEffect = SwapEffect.Sequential,  // bitblt — COLORKEY работает
                AlphaMode = AlphaMode.Ignore,
                Flags = SwapChainFlags.None
            };
            _swapChain = _factory.CreateSwapChainForHwnd(
                _device, hwnd, sc,
                new SwapChainFullscreenDescription { Windowed = true },
                null);
            // v79:MaximumFrameLatency валиден только под waitable; без него каденс = vsync.

            using (ID3D11Texture2D back = _swapChain.GetBuffer<ID3D11Texture2D>(0))
                _rtv = _device.CreateRenderTargetView(back);

            // Шейдеры маркера (inline HLSL, компиляция на старте; см. маркер.hlsl план).
            CreateMarkerPipeline();
        }

        // ---------- МАРКЕР v2.0 (первый GPU-элемент) ----------
        // Круг с чёрной обводкой и тёмным центром — AR-метка (как в AR v1 JS).
        // Позиция задаётся constant buffer (u,v пиксели + размер + цвет) — вся
        // динамика через пер-кадровый CB (архитектурное правило).
        private const string MarkerHlsl = """
            struct VS_IN { float2 pos : POS; };
            struct VS_OUT { float4 pos : SV_POSITION; float2 uv : UV; };
            cbuffer Screen : register(b0) { float2 Viewport; float2 CircleCenter; };
            cbuffer Params : register(b1) { float4 Circle; /* xy=center px, z=radius px, w=unused */ }
            VS_OUT VMain(VS_IN i) {
                VS_OUT o;
                // Квадрат [0..1] растягиваем в px вокруг центра круга.
                float2 px = Circle.xy + (i.pos - 0.5) * 2 * Circle.z;
                float2 ndc = float2(px.x / Viewport.x * 2 - 1, 1 - px.y / Viewport.y * 2);
                o.pos = float4(ndc, 0, 1);
                o.uv = i.pos - 0.5;    // [-0.5..0.5]
                return o;
            }
            float4 PMain(VS_OUT i) : SV_TARGET {
                float r = length(i.uv) * 2;          // 0..1 по радиусу
                float inner = smoothstep(0.30, 0.34, r);  // тёмный центр-точка <0.3
                float edge  = 1 - smoothstep(0.90, 0.99, r);
                float3 col = lerp(float3(0,0,0), float3(1.0,0.45,0.45), inner < 0.5 ? 0 : 1);
                float a = edge;
                if (inner < 0.5) col = 0;
                return float4(col, a);
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
            _cbParams = _device.CreateBuffer(new BufferDescription(
                16, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));

            var bd = new BlendDescription
            {
                AlphaToCoverageEnable = false,
                IndependentBlendEnable = false
            };
            bd.RenderTarget[0] = new RenderTargetBlendDescription
            {
                BlendEnable = true,
                SourceBlend = Blend.SourceAlpha,
                DestinationBlend = Blend.InverseSourceAlpha,
                BlendOperation = BlendOperation.Add,
                SourceBlendAlpha = Blend.One,
                DestinationBlendAlpha = Blend.InverseSourceAlpha,
                BlendOperationAlpha = BlendOperation.Add,
                RenderTargetWriteMask = ColorWriteEnable.All
            };
            _blend = _device.CreateBlendState(bd);
        }

        // Проекция цели (простая v1-совместимая математика; композитный питч — v82).
        // v81: при отсутствие цели — null → рендер рисует центр-прицел (см. RenderFrame).
        // hasProj в текущей версии не используется за пределами (резерв для fade).
        private (float u, float v, bool visible)? ProjectMarker(ArGameState? s)
        {
            if (s?.Target == null) return null;
            var t = s.Target;
            double yaw = s.YawBase * Math.PI * 2 + s.YawHead;
            const double eyeH = 1.9;
            double wx = t.X - s.CamX, wy = (t.Y + 0.5) - (s.CamY + eyeH), wz = t.Z - s.CamZ;
            double fdot = wx * -Math.Sin(yaw) + wz * -Math.Cos(yaw);
            double rdot = wx * Math.Cos(yaw) - wz * Math.Sin(yaw);
            if (fdot <= 0.5) return null;
            double pitch = s.PitchHead * Math.PI * 2;
            double depth = fdot * Math.Cos(pitch) + wy * Math.Sin(pitch);
            double up = wy * Math.Cos(pitch) - fdot * Math.Sin(pitch);
            double fovTan = Math.Tan(75.0 * Math.PI / 180 / 2);
            double f = _width * 0.5 / fovTan;
            double u = _width / 2.0 + f * (rdot / depth);
            double v = _height / 2.0 - f * (up / depth);
            if (!double.IsFinite(u) || !double.IsFinite(v)) return null;
            return ((float)u, (float)v, true);
        }

        public void WaitForNextFrame()
        {
            // v79: waitable-объекта нет (bitblt). Каденс задаёт vsync Present(1)
            // внутри RenderFrame — этот метод теперь no-op (оставлен для API).
        }

        /// <summary>
        /// ОДИН кадр. Всё между «взять latest» и Present — лёгкое (архитектура).
        /// </summary>
        public void RenderFrame()
        {
            if (_device == null || _context == null || _rtv == null || _swapChain == null) return;

            // 1) latest GameState (без блокировок, без очереди устаревших).
            var state = ArBridge.Game.Latest;

            var ctx = _context;
            ctx.ClearRenderTargetView(_rtv, new Color4(0, 0, 0, 1)); // чёрный = COLORKEY-прозрачность

            // 2) Маркер цели (круг): вся динамика через per-frame constant buffers.
            //    v81: цели может не быть — тогда рисуем компактный прицел по центру
            //    (визуальное подтверждение «рендер жив», иначе пустой кадр и
            //    цветовое окно неотличимо от выключенного).
            float mkU = _width / 2f, mkV = _height / 2f, mkR = 10f;
            bool hasProj = false;
            var proj = ProjectMarker(state);
            if (proj.HasValue)
            {
                mkU = proj.Value.u; mkV = proj.Value.v; mkR = 33f; hasProj = true;
            }
            if (_vs != null && _il != null && _vb != null)
            {
                // CB0 Screen: (W, H, 0, 0) — небезопасная запись по DataPointer.
                unsafe
                {
                    var mp0 = ctx.Map(_cbScreen!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                    var p0 = (float*)mp0.DataPointer;
                    p0[0] = _width; p0[1] = _height; p0[2] = 0f; p0[3] = 0f;
                    ctx.Unmap(_cbScreen!);

                    // CB1 Params: (u, v, radius, 0) — центр в пикселях + радиус.
                    var mp1 = ctx.Map(_cbParams!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                    var p1 = (float*)mp1.DataPointer;
                    p1[0] = mkU; p1[1] = mkV;
                    p1[2] = mkR; p1[3] = 0f;
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
            _swapChain!.Present(1, 0u);   // vsync: каденс + отсутствие спин-цикла (bitblt; waitable недоступен)
        }

        public void Resize(int width, int height)
        {
            _width = width; _height = height;
            _rtv?.Dispose();
            _swapChain!.ResizeBuffers(0, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
            using (ID3D11Texture2D back = _swapChain.GetBuffer<ID3D11Texture2D>(0))
                _rtv = _device!.CreateRenderTargetView(back);
        }

        public void Dispose()
        {
            _rtv?.Dispose();
            _vb?.Dispose();
            _vs?.Dispose();
            _ps?.Dispose();
            _swapChain?.Dispose();
            _device?.Dispose();
            _context?.Dispose();
            _factory?.Dispose();
        }
    }
}