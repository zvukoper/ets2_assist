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
            CreateTextPipeline();   // v83: текстовый конвейер (имя/дистанция/углы)
            CreateTestPipeline();   // v84: тестовый объект (плавный кружок, проверка рендера)
            CreateBoxPipeline();    // v85: боксовый конвейер (центр. пиксель, линии креста pin)
        }

        // ---------- МАРКЕР v2.0 (первый GPU-элемент) ----------
        // Круг с чёрной обводкой и тёмным центром — AR-метка (как в AR v1 JS).
        // Позиция задаётся constant buffer (u,v пиксели + размер + цвет) — вся
        // динамика через пер-кадровый CB (архитектурное правило).
        private const string MarkerHlsl = """
            struct VS_IN { float2 pos : POS; };
            struct VS_OUT { float4 pos : SV_POSITION; float2 uv : UV; };
            cbuffer Screen : register(b0) { float2 Viewport; float2 CircleCenter; };
            // v85: цвет и альфа задаются на C# (цвет категории точки, как в AR v1).
            cbuffer Params : register(b1) { float4 Circle; float4 Tint; }; /* Circle: xy=center px, z=radius px, w=alpha; Tint: rgb */
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
                float r = length(i.uv) * 2;                     // 0..1 по радиусу
                float dot0  = 1 - smoothstep(0.16, 0.22, r);    // тёмный центр (как AR v1)
                float ring  = smoothstep(0.74, 0.84, r);        // чёрная обводка
                float edge  = 1 - smoothstep(0.92, 1.00, r);    // за краем — прозрачно
                float3 col = lerp(Tint.rgb, float3(0, 0, 0), max(dot0, ring));
                return float4(col, edge * Circle.w);
            }
            """;

        private ID3D11VertexShader? _vs;
        private ID3D11PixelShader? _ps;
        private ID3D11InputLayout? _il;
        private ID3D11Buffer? _vb, _cbScreen, _cbParams;
        private ID3D11BlendState? _blend;

        // ==================== ТЕСТОВЫЙ ОБЪЕКТ (v84) ====================
        // Плавно движущийся кружок в левом верхнем углу, НИ к чему не привязан.
        // Цель — проверить эффективность/плавность рендера AR2 (vsync-каденс).
        // Позиция = синусоида по времени (влево-вправо), радиус 24px, цвет голубой.
        private const string TestHlsl = """
            struct VS_IN { float2 pos : POS; };
            struct VS_OUT { float4 pos : SV_POSITION; float2 uv : UV; };
            cbuffer Screen : register(b0) { float2 Viewport; float2 _pad; };
            cbuffer Params : register(b1) { float4 C; /* xy=center px, z=radius px, w=unused */ }
            VS_OUT VMain(VS_IN i) {
                VS_OUT o;
                float2 px = C.xy + (i.pos - 0.5) * 2 * C.z;
                o.pos = float4(px.x / Viewport.x * 2 - 1, 1 - px.y / Viewport.y * 2, 0, 1);
                o.uv = i.pos - 0.5;
                return o;
            }
            float4 PMain(VS_OUT i) : SV_TARGET {
                float r = length(i.uv) * 2;
                float a = 1 - smoothstep(0.90, 0.99, r);
                return float4(0.3, 0.8, 1.0, a);
            }
            """;
        private ID3D11VertexShader? _tvs2;
        private ID3D11PixelShader? _tps2;
        private ID3D11InputLayout? _til2;
        private ID3D11Buffer? _tvb2, _tcbParams2;
        private long _testStartMs;

        private void CreateTestPipeline()
        {
            var vsRes = Compiler.Compile(TestHlsl, "VMain", "test", "vs_5_0", out var vsBlob, out _);
            if (vsRes.Failure || vsBlob == null) return;
            var psRes = Compiler.Compile(TestHlsl, "PMain", "test", "ps_5_0", out var psBlob, out _);
            if (psRes.Failure || psBlob == null) return;
            _tvs2 = _device!.CreateVertexShader(vsBlob, null);
            _tps2 = _device.CreatePixelShader(psBlob, null);
            _til2 = _device.CreateInputLayout(new[]
            {
                new InputElementDescription("POS", 0, Format.R32G32_Float, 0, 0)
            }, vsBlob);
            var verts = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1),
                new Vector2(0, 1), new Vector2(1, 0), new Vector2(1, 1),
            };
            _tvb2 = _device.CreateBuffer<Vector2>(verts, BindFlags.VertexBuffer,
                ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None);
            _tcbParams2 = _device.CreateBuffer(new BufferDescription(
                16, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
            _testStartMs = Environment.TickCount64;
        }

        // Рисует тестовый кружок: центр по синусоиде времени (плавно влево-вправо).
        private void DrawTestObject()
        {
            if (_tvs2 == null || _tps2 == null || _til2 == null || _tvb2 == null || _tcbParams2 == null) return;
            double t = (Environment.TickCount64 - _testStartMs) / 1000.0;
            // Плавная анимация: x = 120 + 90*sin(2π·0.5·t), y = 90 (левый верхний угол).
            float cx = (float)(120 + 90 * Math.Sin(2 * Math.PI * 0.5 * t));
            float cy = 90f;
            float r = 24f;
            var ctx = _context!;
            unsafe
            {
                var mp = ctx.Map(_tcbParams2!, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                var p = (float*)mp.DataPointer;
                p[0] = cx; p[1] = cy; p[2] = r; p[3] = 0f;
                ctx.Unmap(_tcbParams2!);
            }
            ctx.IASetInputLayout(_til2);
            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.IASetVertexBuffer(0, _tvb2, 8, 0);
            ctx.VSSetShader(_tvs2);
            ctx.VSSetConstantBuffer(0, _cbScreen);
            ctx.VSSetConstantBuffer(1, _tcbParams2);
            ctx.PSSetShader(_tps2!);
            ctx.PSSetConstantBuffer(0, _cbScreen);
            ctx.PSSetConstantBuffer(1, _tcbParams2);
            ctx.OMSetBlendState(_blend);
            ctx.OMSetRenderTargets(_rtv, null);
            ctx.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, _width, _height));
            ctx.Draw(6, 0);
        }

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
                return float4(Tint.rgb, Tint.w);
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
        // ================================================================
        private (float u, float v, bool inFront, double dist, double depth)? ProjectPoint(
            double wx, double wy, double wz, ArGameState? s)
        {
            if (s == null) return null;
            // YawHead в ArGameState — ДОЛЯ ОБОРОТА (как head.offset в TruckTel),
            // поэтому ×2π — как в JS (c.yawHead уже рад, тут приводим к тому же).
            double yaw = s.YawBase * Math.PI * 2 + s.YawHead * Math.PI * 2;
            const double eyeH = 1.9;
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

        /// <summary>История проекции цели для отрисовки.</summary>
        public readonly record struct MarkerProjection(float U, float V, bool Visible, double Dist);

        public void WaitForNextFrame()
        {
            // v79: waitable-объекта нет (bitblt). Каденс задаёт vsync Present(1)
            // внутри RenderFrame — этот метод теперь no-op (оставлен для API).
        }

        /// <summary>ОДИН кадр. Всё между «взять latest» и Present — лёгкое (архитектура).</summary>
        public void RenderFrame()
        {
            if (_device == null || _context == null || _rtv == null || _swapChain == null) return;

            // 1) latest GameState (без блокировок, без очереди устаревших).
            var state = ArBridge.Game.Latest;

            var ctx = _context;
            ctx.ClearRenderTargetView(_rtv, new Color4(0, 0, 0, 1)); // чёрный = COLORKEY-прозрачность

            // v84: тестовый объект (плавный кружок в левом верхнем углу) — проверка плавности.
            DrawTestObject();

            // ============================================================
            // v85: ПРИЦЕЛЬНЫЙ ПИКСЕЛЬ (как render() в ar_hud.js): fillRect
            // 1.5×1.5, rgba(255,255,255,0.45), ВСЕГДА (независимо от цели).
            // ============================================================
            DrawBox(_width / 2f - 0.75f, _height / 2f - 0.75f, 1.5f, 1.5f, 1f, 1f, 1f, 0.45f);

            float m = 70f;   // edgeMargin (CFG / ar_hud.js)

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
                    double d = Math.Max(0, pr.Value.dist);
                    float size = d <= 10 ? 33f : d >= 500 ? 15f :
                        (float)(33 + (15 - 33) * (d - 10) / (500 - 10));
                    float alpha = d <= 500 ? 1f :
                        Math.Clamp(1f - (float)((d - 500) / (1500 - 500)), 0.12f, 1f);

                    var (mr, mg, mb) = ColorFor(tg);
                    DrawCircle(_smTarget.U, _smTarget.V, size / 2f, mr, mg, mb, alpha);

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
                            float tv = _smTarget.V + 34f;
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
                float4 c = Txt.Sample(Smpl, i.uv);   // BGRA: текст + обводка + альфа
                c.w *= TintW.w;                      // v85: множитель альфы (затухание по дистанции)
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
                SourceBlend = Blend.SourceAlpha,
                DestinationBlend = Blend.InverseSourceAlpha,
                BlendOperation = BlendOperation.Add,
                SourceBlendAlpha = Blend.One,
                DestinationBlendAlpha = Blend.InverseSourceAlpha,
                BlendOperationAlpha = BlendOperation.Add,
                RenderTargetWriteMask = ColorWriteEnable.All
            };
            _tblend = _device.CreateBlendState(bd);
        }

        // GDI-пререндер строки в BGRA-спрайт (белый/цветной текст + чёрная обводка 8-напр.).
        private TextSprite MakeTextSprite(string text, Font font, System.Drawing.Color fill)
        {
            int w, h;
            using (var mg = Graphics.FromHwnd(IntPtr.Zero))
            {
                var sz = mg.MeasureString(text, font);
                w = (int)Math.Ceiling(sz.Width) + 10;
                h = (int)Math.Ceiling(sz.Height) + 10;
            }
            using var bmp = new Bitmap(Math.Max(2, w), Math.Max(2, h), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                for (int dx = -2; dx <= 2; dx += 2)
                    for (int dy = -2; dy <= 2; dy += 2)
                    {
                        if (dx == 0 && dy == 0) continue;
                        using var sh = new SolidBrush(System.Drawing.Color.FromArgb(200, 0, 0, 0));
                        g.DrawString(text, font, sh, 5 + dx, 5 + dy);
                    }
                using var br = new SolidBrush(fill);
                g.DrawString(text, font, br, 5, 5);
            }
            var rect = new System.Drawing.Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
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

        // Кэш по контенту: пересоздаём спрайт ТОЛЬКО когда строка сменилась.
        private void EnsureText(ref TextSprite? sprite, string text, Font font, System.Drawing.Color fill)
        {
            if (sprite != null && sprite.Text == text) return;
            try
            {
                sprite?.Dispose();
                sprite = MakeTextSprite(text, font, fill);
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
            _txt1?.Dispose(); _txt2?.Dispose(); _pinTxt?.Dispose();
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
            _tcbParams2?.Dispose();
            _tvb2?.Dispose();
            _til2?.Dispose();
            _tps2?.Dispose();
            _tvs2?.Dispose();
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