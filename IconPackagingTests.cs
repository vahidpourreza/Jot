using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Jot;
public partial class MainWindow
{
    private async Task VerifyApprovedIconAssets(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="approved-icon-"+name,passed});
        var master=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"assets","jot-color.png"));
        // This is the user's selected artwork, not a generative redraw.
        Check("color-master-is-exact-approved-image",Convert.ToHexString(SHA256.HashData(master))=="0125FCDFAB8CC061D017D12319240694B6333B0D42E3A4ECA2E924FB8AE4B7D2");
        VerifyTrayFold(checks);
        var executable=File.ReadAllBytes(Environment.ProcessPath!);
        var packed=AppIcon.LoadColor();using var reader=new BinaryReader(new MemoryStream(packed));
        reader.ReadUInt16();reader.ReadUInt16();int count=reader.ReadUInt16();
        for(int i=0;i<count;i++)
        {
            int size=reader.ReadByte();if(size==0)size=256;reader.ReadBytes(7);
            int length=checked((int)reader.ReadUInt32()),offset=checked((int)reader.ReadUInt32());
            Check("executable-contains-current-"+size+"px-frame",executable.AsSpan().IndexOf(packed.AsSpan(offset,length))>=0);
        }
        var home=session.Home();await home.WaitFor("window.jotReady===true");
        using var masterStream=new MemoryStream(master);using var masterImage=System.Drawing.Image.FromStream(masterStream);
        await home.WaitFor("document.querySelector('.library-logo').complete&&document.querySelector('.library-logo').naturalWidth>0");
        Check("home-loads-current-color-master",await home.Script("document.querySelector('.library-logo').naturalWidth==="+masterImage.Width+"&&document.querySelector('.library-logo').naturalHeight==="+masterImage.Height+"&&document.querySelector('.library-logo').currentSrc.endsWith('/assets/jot-color.png')")=="true");
        foreach(var theme in new[]{"dark","light"})
        {
            await store.SavePreferences(JsonSerializer.SerializeToElement(new{theme}));await session.Changed();
            await home.WaitFor("document.documentElement.dataset.theme==='"+theme+"'");
            await home.Capture("approved-logo-home-"+theme);
        }
        Check("offscreen-no-user-windows-or-renderer-errors",session.Windows.All(w=>w.Left< -10000&&w.Top< -10000&&!w.ShowActivated&&!w.ShowInTaskbar&&!w.Topmost&&w.Opacity==0&&w.RuntimeErrors.Count==0));
    }

    private void VerifyTrayFold(List<object> checks)
    {
        void Check(string name,bool passed)=>checks.Add(new{name="tray-fold-"+name,passed});
        using var mask=new System.Drawing.Bitmap(Path.Combine(AppContext.BaseDirectory,"assets","jot-tray-mask.png"));
        // Interior probes guard against the former hollow fold; the separator
        // remains transparent, not a dark line that disappears in one theme.
        byte Alpha(double x,double y)=>mask.GetPixel((int)(mask.Width*x),(int)(mask.Height*y)).A;
        Check("solid-flap-in-master",Alpha(.54,.79)>=250&&Alpha(.565,.77)>=250);
        Check("transparent-separator",Alpha(.514,.80)<=16);
        Check("transparent-exterior",Alpha(.02,.02)==0&&Alpha(.60,.86)==0);
        int left=mask.Width,top=mask.Height,right=0,bottom=0;
        for(int y=0;y<mask.Height;y++)for(int x=0;x<mask.Width;x++)
            if(mask.GetPixel(x,y).A>32){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
        foreach(int size in new[]{16,20,24,32,48,64,128,256})
        {
            // Read the exact PNG frame: native Icon size selection can choose
            // a smaller frame (notably for the ICO width byte 0 = 256).
            using var dark=ReadFrame(AppIcon.LoadMonochrome(true),size);
            using var light=ReadFrame(AppIcon.LoadMonochrome(false),size);
            bool sameAlpha=true,cleanInk=true;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                var d=dark.GetPixel(x,y);var l=light.GetPixel(x,y);
                sameAlpha&=d.A==l.A;
                if(d.A>=250)cleanInk&=Math.Abs(d.R-30)<=2&&Math.Abs(d.G-30)<=2&&Math.Abs(d.B-30)<=2;
                if(l.A>=250)cleanInk&=Math.Abs(l.R-245)<=2&&Math.Abs(l.G-245)<=2&&Math.Abs(l.B-245)<=2;
            }
            int padding=Math.Max(1,(int)Math.Round(size*.06));
            double scale=(size-2.0*padding)/Math.Max(right-left+1,bottom-top+1);
            int width=(int)Math.Round((right-left+1)*scale),height=(int)Math.Round((bottom-top+1)*scale);
            int px=(int)((size-width)/2+(mask.Width*.55-left)*width/(right-left+1));
            int py=(int)((size-height)/2+(mask.Height*.785-top)*height/(bottom-top+1));
            Check(size+"px-solid-fold",dark.GetPixel(px,py).A>=128&&light.GetPixel(px,py).A>=128);
            Check(size+"px-identical-alpha",sameAlpha);
            Check(size+"px-uniform-ink",cleanInk);
        }

        static System.Drawing.Bitmap ReadFrame(byte[] bytes,int size)
        {
            using var reader=new BinaryReader(new MemoryStream(bytes));
            reader.ReadUInt16();reader.ReadUInt16();int count=reader.ReadUInt16();
            for(int i=0;i<count;i++)
            {
                int width=reader.ReadByte();if(width==0)width=256;reader.ReadBytes(7);
                int length=checked((int)reader.ReadUInt32()),offset=checked((int)reader.ReadUInt32());
                if(width!=size)continue;
                using var stream=new MemoryStream(bytes,offset,length);
                using var frame=new System.Drawing.Bitmap(stream);
                return new System.Drawing.Bitmap(frame);
            }
            throw new InvalidDataException("Missing tray ICO frame: "+size);
        }
    }
}
