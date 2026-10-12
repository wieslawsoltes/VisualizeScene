using VisualizeScene.Core;

static class ScrollTests
{
    static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    static void Near(double a, double b) { if (!double.IsFinite(a) || Math.Abs(a-b) > 1e-7 * Math.Max(1, Math.Abs(b))) throw new Exception($"{a:R} != {b:R}"); }
    static void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected rejection"); }
    public static void Run(Action<string, Action> test)
    {
        test("Scrolling: normal ranges, thumb ratio and pixel line/page increments", () => {
            var m = ViewportScrollModel.Calculate(new(0,0,2000,1000), new(new(100,50),2), 800,400);
            Near(m.Horizontal.MinimumOrigin,-20); Near(m.Horizontal.MaximumOrigin,1620);
            Near(m.Horizontal.GetOrigin(m.Horizontal.Value),100); Near(m.Vertical.GetOrigin(m.Vertical.Value),50);
            Near(m.Horizontal.SmallChange*1640*2,24); Near(m.Horizontal.LargeChange*1640*2,680);
            Near(m.Horizontal.ViewportSize/(1+m.Horizontal.ViewportSize),400d/2040);
        });
        test("Scrolling: fitted content disables bars without moving the camera", () => {
            var m = ViewportScrollModel.Calculate(new(50,50,100,100),new(new(),1),200,200);
            Check(!m.Horizontal.CanScroll && !m.Vertical.CanScroll); Near(m.Horizontal.GetOrigin(.7),0);
        });
        test("Scrolling: negative coordinates and off-content camera positions stay reachable", () => {
            var m = ViewportScrollModel.Calculate(new(-500,-800,100,200),new(new(900,700),1),300,200);
            Near(m.Horizontal.GetOrigin(0),-540); Near(m.Horizontal.GetOrigin(1),900);
            Near(m.Vertical.GetOrigin(0),-840); Near(m.Vertical.GetOrigin(1),700);
        });
        test("Scrolling: captured ranges stay stable for an entire thumb gesture", () => {
            var m = ViewportScrollModel.Calculate(new(0,0,1000,800),new(new(-400,-400),1),300,200);
            var step = m.Horizontal.GetOrigin(.25); var later = m.Horizontal.GetOrigin(.75);
            Near(later-step,(m.Horizontal.MaximumOrigin-m.Horizontal.MinimumOrigin)*.5);
            Near(m.Horizontal.GetOrigin(m.Horizontal.Value),-400);
        });
        test("Scrolling: oversized view, empty scene and zero dimensions are finite", () => {
            foreach (var size in new[] {0d,1,100,2000}) {
                var m=ViewportScrollModel.Calculate(new(0,0,0,0),new(new(-100,-100),.05),size,size);
                Check(double.IsFinite(m.Horizontal.Value) && double.IsFinite(m.Horizontal.ViewportSize));
                if(size==0) Check(!m.Horizontal.CanScroll);
            }
        });
        test("Scrolling: out-of-range values clamp, nonfinite values reject", () => {
            var a=ViewportScrollModel.Calculate(new(0,0,1000,1000),new(new(),1),100,100).Horizontal;
            Near(a.GetOrigin(-100),a.MinimumOrigin); Near(a.GetOrigin(100),a.MaximumOrigin);
            Throws(()=>a.GetOrigin(double.NaN)); Throws(()=>a.GetOrigin(double.PositiveInfinity));
        });
        test("Scrolling: all zoom limits preserve current origin and correct line distance", () => {
            foreach(var zoom in new[]{.05,.35,1,2,8}) {
                var a=ViewportScrollModel.Calculate(new(-1e6,0,2e6,100),new(new(123.125,-40),zoom),800,400).Horizontal;
                Near(a.GetOrigin(a.Value),123.125); Near((a.MaximumOrigin-a.MinimumOrigin)*a.SmallChange*zoom,24);
            }
        });
        test("Scrolling: invalid geometry, negative sizes and overflowing extents reject", () => {
            Throws(()=>ViewportScrollModel.Calculate(new(0,0,-1,1),new(new(),1),800,400));
            foreach(var value in new[]{double.NaN,double.PositiveInfinity,-1})
                Throws(()=>ViewportScrollModel.Calculate(new(0,0,100,100),new(new(),1),value,400));
            Throws(()=>ViewportScrollModel.Calculate(new(0,0,100,100),new(new(),0),800,400));
            Throws(()=>ViewportScrollModel.Calculate(new(0,0,100,100),new(new(),1),800,400,lineStep:0));
            Throws(()=>ViewportScrollModel.Calculate(new(-double.MaxValue,0,double.MaxValue,10),new(new(double.MaxValue,0),1),800,400));
        });
        test("Scrolling: 10000 randomized ranges preserve origin and monotone mapping", () => {
            var random=new Random(155); for(var i=0;i<10000;i++) {
                var origin=new PointD(random.Next(-100000,100000),random.Next(-100000,100000));
                var m=ViewportScrollModel.Calculate(new(random.Next(-10000,10000),-10000,random.Next(1,100000),50000),new(origin,.05+random.NextDouble()*7.95),random.Next(1,2000),900);
                foreach(var a in new[]{m.Horizontal,m.Vertical}) {
                    Near(a.GetOrigin(a.Value),a.Origin); Check(a.GetOrigin(.1)<=a.GetOrigin(.9));
                    Check(a.Value>=0 && a.Value<=1 && a.SmallChange>=0 && a.SmallChange<=1);
                }
            }
        });
        test("Scrolling: camera updates allocate no managed memory after warmup", () => {
            var bounds=new RectD(-10000,-10000,20000,20000); var view=new ViewportState(new(100,200),1);
            for(var i=0;i<1000;i++) _=ViewportScrollModel.Calculate(bounds,view,800,600);
            var before=GC.GetAllocatedBytesForCurrentThread(); double sum=0;
            for(var i=0;i<10000;i++) sum+=ViewportScrollModel.Calculate(bounds,view,800,600).Horizontal.Value;
            var allocated=GC.GetAllocatedBytesForCurrentThread()-before; Check(allocated==0 && sum>0);
        });
    }
}
