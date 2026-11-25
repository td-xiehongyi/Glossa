using System;
using System.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Automation;
using System.Windows;

class Selection {
    [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr info);
    [DllImport("user32")] static extern uint GetClipboardSequenceNumber();
    [DllImport("user32")] static extern IntPtr GetClipboardOwner();
    [DllImport("user32")] static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32")] static extern bool CloseClipboard();
    [DllImport("user32")] static extern uint EnumClipboardFormats(uint fmt);
    [DllImport("user32")] static extern int CountClipboardFormats();
    [DllImport("user32")] static extern bool EmptyClipboard();
    [DllImport("user32")] static extern IntPtr GetClipboardData(uint fmt);
    [DllImport("user32")] static extern IntPtr SetClipboardData(uint fmt,IntPtr data);
    [DllImport("kernel32")] static extern IntPtr GlobalAlloc(uint flags,UIntPtr bytes);
    [DllImport("kernel32")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32")] static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32")] static extern IntPtr GlobalFree(IntPtr h);
    [DllImport("kernel32")] static extern UIntPtr GlobalSize(IntPtr h);
    public string Text; public Rect Bounds=Rect.Empty;
    public static uint Pid(IntPtr h){uint p;GetWindowThreadProcessId(h,out p);return p;}
    [DllImport("user32")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32")] static extern IntPtr WindowFromPoint(System.Drawing.Point point);
    public static Selection ReadDirect(IntPtr window,Point point) {
        bool qq=false;
        try {
            using(var process=System.Diagnostics.Process.GetProcessById((int)Pid(window)))
                qq=string.Equals(process.ProcessName,"QQ",StringComparison.OrdinalIgnoreCase);
            var focused=AutomationElement.FocusedElement;
            if(focused!=null && focused.Current.IsPassword)return new Selection();
            // QQ can leave keyboard focus in the editor while text is selected in a message.
            if(qq && GetAncestor(WindowFromPoint(new System.Drawing.Point((int)point.X,(int)point.Y)),2)==window){
                try{
                    var hit=ReadPath(AutomationElement.FromPoint(point),window);
                    if(hit!=null && (hit.Text!=null || hit.BlockCopy))return hit;
                }catch(ElementNotAvailableException){}
            }
            var result=ReadPath(focused,window);
            if(qq && result!=null && !result.BlockCopy && result.Text==null)return null;
            return result;
        }catch(Exception){return null;}
    }
    bool BlockCopy;
    static Selection ReadPath(AutomationElement e,IntPtr window) {
            bool emptySelection=false;
            for(int i=0;e!=null && i<8;i++,e=TreeWalker.ControlViewWalker.GetParent(e)){
                if(e.Current.ProcessId!=Pid(window))break; if(e.Current.IsPassword)return new Selection{BlockCopy=true};
                object pattern;
                if(!e.TryGetCurrentPattern(TextPattern.Pattern,out pattern))continue;
                var ranges=((TextPattern)pattern).GetSelection();
                emptySelection=true; if(ranges.Length==0)continue;
                var selected=string.Join("\n",ranges.Select(r=>r.GetText(4001))).Trim();
                if(selected.Length==0)continue;
                var result=new Selection { Text=selected };
                try{
                    var rects=ranges[0].GetBoundingRectangles();
                    if(rects.Length>0)result.Bounds=rects[rects.Length-1];
                }catch(InvalidOperationException){}catch(NotSupportedException){}
                return result;
            }
            if(emptySelection)return new Selection();
        return null;
    }
    static string ClipboardText(){
        var h=GetClipboardData(13);
        if(h==IntPtr.Zero || GlobalSize(h).ToUInt64()>8*1024*1024)return null;
        var p=GlobalLock(h);if(p==IntPtr.Zero)return null;
        try{return Marshal.PtrToStringUni(p);}finally{GlobalUnlock(h);}
    }
    public static async Task<Selection> CopyFallback(IntPtr original,IntPtr app,Func<int> activity,CancellationToken token){
        for(int i=0;i<50 && new[]{16,17,18,91,92}.Any(k=>GetAsyncKeyState(k)<0);i++) await Task.Delay(10,token);
        token.ThrowIfCancellationRequested();
        if(original!=GetForegroundWindow() || new[]{16,17,18,91,92}.Any(k=>GetAsyncKeyState(k)<0))return null;
        int epoch=activity(); uint before; IntPtr backup=IntPtr.Zero;
        if(!OpenClipboard(app))return null;
        try{
            before=GetClipboardSequenceNumber();
            if(CountClipboardFormats()>0){
                for(uint f=EnumClipboardFormats(0);f!=0;f=EnumClipboardFormats(f))
                    if(f!=1 && f!=7 && f!=13 && f!=16)throw new InvalidOperationException("剪贴板含有无法可靠备份的格式，已保留原内容。");
                var old=ClipboardText();if(old==null)return null;
                var bytes=Encoding.Unicode.GetBytes(old+"\0");
                backup=GlobalAlloc(2,(UIntPtr)bytes.Length);
                if(backup==IntPtr.Zero)return null;
                var ptr=GlobalLock(backup);
                if(ptr==IntPtr.Zero){GlobalFree(backup);return null;}
                Marshal.Copy(bytes,0,ptr,bytes.Length);GlobalUnlock(backup);
            }
        }finally{CloseClipboard();}
        try{
            if(before!=GetClipboardSequenceNumber() || original!=GetForegroundWindow())return null;
            keybd_event(17,0,0,UIntPtr.Zero);keybd_event(67,0,0,UIntPtr.Zero);
            keybd_event(67,0,2,UIntPtr.Zero);keybd_event(17,0,2,UIntPtr.Zero);
            // Finish the clipboard transaction even if the translation request is cancelled.
            for(int i=0;i<35;i++){
                await Task.Delay(12);
                if(activity()!=epoch || original!=GetForegroundWindow())return null;
                if(GetClipboardSequenceNumber()==before || !OpenClipboard(app))continue;
                try{
                    if(Pid(GetClipboardOwner())!=Pid(original))return null;
                    var s=ClipboardText();
                    if(!EmptyClipboard())return null;
                    if(backup!=IntPtr.Zero){if(SetClipboardData(13,backup)==IntPtr.Zero)throw new Exception("无法恢复剪贴板。");backup=IntPtr.Zero;}
                    return string.IsNullOrWhiteSpace(s)?null:new Selection{Text=s.Trim()};
                }finally{CloseClipboard();}
            }
            return null;
        }finally{if(backup!=IntPtr.Zero)GlobalFree(backup);}
    }
}

