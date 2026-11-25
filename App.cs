using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Automation;
using F=System.Windows.Forms;

class App : Application {
    [DllImport("user32")] static extern bool RegisterHotKey(IntPtr w,int id,uint mod,uint key);
    [DllImport("user32")] static extern bool UnregisterHotKey(IntPtr w,int id);
    delegate IntPtr Hook(int code,IntPtr msg,IntPtr data);
    [DllImport("user32",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int type,Hook fn,IntPtr module,uint thread);
    [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32")] static extern IntPtr CallNextHookEx(IntPtr h,int code,IntPtr msg,IntPtr data);
    [DllImport("user32")] static extern bool GetWindowRect(IntPtr hwnd,out WinRect rect);
    [StructLayout(LayoutKind.Sequential)] struct WinRect { public int left,top,right,bottom; }
    readonly LocalTranslator translator=new LocalTranslator();
    readonly SemaphoreSlim capture=new SemaphoreSlim(1,1);
    HwndSource sink; F.NotifyIcon tray; Window card;
    TextBlock body; ScrollViewer scroll; Button copy,retry,language;
    System.Windows.Shapes.Path tip;
    double selectionTop;
    string source,target,answer; CancellationTokenSource current;
    IntPtr mouseHook,keyHook; Hook mouseFn,keyFn; int activity; bool copying;
    int hotkeyId=1;
    bool keyboardRegistered;
    uint pressedSide;
    [DllImport("user32")] static extern short GetAsyncKeyState(int key);
    uint mods=0,vk=5; Point anchor;
    string config=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"划词翻译","hotkey.txt");
    [STAThread] static void Main(){bool first;using(var single=new Mutex(true,"Local\\SimpleSelectionTranslator_01",out first)){if(first)new App().Run();}}
    protected override void OnStartup(StartupEventArgs e){
        base.OnStartup(e);ShutdownMode=ShutdownMode.OnExplicitShutdown;
        appearance=GlassAppearance.Load(AppearancePath);
        sink=new HwndSource(new HwndSourceParameters("SelectionTranslatorMessageSink"){WindowStyle=0,Width=0,Height=0});
        sink.AddHook(Message);
        if(File.Exists(config)){try{var parts=File.ReadAllText(config).Split(',');uint m,k;if(parts.Length==2&&uint.TryParse(parts[0],out m)&&uint.TryParse(parts[1],out k)&&ValidBinding(m,k)){mods=m;vk=k;}}catch(Exception){}}
        tray=new F.NotifyIcon {Icon=System.Drawing.Icon.ExtractAssociatedIcon(F.Application.ExecutablePath),Text="Glossa · 划词翻译",Visible=true,ContextMenuStrip=new TrayMenu(ShowSettings,()=>Shutdown())};
        tray.DoubleClick+=(s,a)=>ShowSettings();
        mouseFn=Mouse;keyFn=Keyboard;
        UpdateMouse();
        if(!IsSide(vk))keyboardRegistered=RegisterHotKey(sink.Handle,hotkeyId,mods|0x4000,vk);
        if(IsSide(vk) ? mouseHook==IntPtr.Zero : !keyboardRegistered)MessageBox.Show("无法启用翻译快捷键，请在托盘菜单中修改。","Glossa");
    }
    static bool IsSide(uint k){return k==5 || k==6;}
    static bool ValidBinding(uint m,uint k){
        return m<8 && (IsSide(k) || m!=0 && (k>=65&&k<=90 || k>=48&&k<=57 || k>=112&&k<=123));
    }
    static string KeyName(uint k){
        if(IsSide(k))return SideName(k);
        if(k>=112)return "F"+(k-111);
        return ((char)k).ToString();
    }
    bool ActivateBinding(uint m,uint k){
        if(!ValidBinding(m,k))return false;
        if(IsSide(k)){
            if(mouseHook==IntPtr.Zero)mouseHook=SetWindowsHookEx(14,mouseFn,GetModuleHandle(null),0);
            if(mouseHook==IntPtr.Zero)return false;
        }else{
            if(keyboardRegistered && mods==m && vk==k)return true;
            int next=hotkeyId==1?3:1;
            if(!RegisterHotKey(sink.Handle,next,m|0x4000,k))return false;
            if(keyboardRegistered)UnregisterHotKey(sink.Handle,hotkeyId);
            hotkeyId=next;
        }
        if(IsSide(k) && keyboardRegistered)UnregisterHotKey(sink.Handle,hotkeyId);
        keyboardRegistered=!IsSide(k);
        mods=m;vk=k;UpdateMouse();
        return true;
    }
    Window settingsWindow;
    void ShowSettings(){
        if(settingsWindow!=null){settingsWindow.Activate();return;}
        Dismiss();
        var w=new Window{Title="Glossa 设置",Width=Math.Min(654,SystemParameters.WorkArea.Width),Height=Math.Min(600,SystemParameters.WorkArea.Height),
            MaxHeight=SystemParameters.WorkArea.Height,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,
            Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterScreen,ResizeMode=ResizeMode.NoResize};
        Border shell,panel;
        using(var stream=typeof(App).Assembly.GetManifestResourceStream("Settings.xaml"))
            shell=(Border)System.Windows.Markup.XamlReader.Load(stream);
        using(var stream=typeof(App).Assembly.GetManifestResourceStream("Hotkeys.xaml"))
            panel=(Border)System.Windows.Markup.XamlReader.Load(stream);
        w.Content=shell;
        ((DockPanel)shell.FindName("TitleBar")).MouseLeftButtonDown+=(s,e)=>w.DragMove();
        ((Button)shell.FindName("CloseButton")).Click+=(s,e)=>w.Close();
        var pages=(TabControl)shell.FindName("Pages");
        ((TabItem)pages.Items[0]).Content=panel;
        ((TabItem)pages.Items[1]).Content=appearance.CreatePage(w,AppearancePath,chosen=>{
            appearance=chosen;
            if(card!=null){card.Close();card=null;}
        });
        var rows=(StackPanel)panel.FindName("KeyboardRows");
        var caps=(WrapPanel)panel.FindName("SelectionCaps");
        var status=(TextBlock)panel.FindName("Status");
        var save=(Button)panel.FindName("SaveButton");
        uint m=mods,k=vk;
        var buttons=new System.Collections.Generic.List<System.Windows.Controls.Primitives.ToggleButton>();
        Action<uint> refresh=held=>{
            caps.Children.Clear();
            foreach(var b in buttons){
                int code=(int)b.Tag;
                bool selected=code<0?(m&(uint)-code)!=0:k==(uint)code;
                b.IsChecked=code<0 && held!=0?(held&(uint)-code)!=0:selected;
            }
            foreach(var name in new[]{(m&2)!=0?"Ctrl":null,(m&1)!=0?"Alt":null,(m&4)!=0?"Shift":null,KeyName(k)}){
                if(name==null)continue;
                if(caps.Children.Count!=0)caps.Children.Add(new TextBlock{Text="+",Margin=new Thickness(5,0,5,0),VerticalAlignment=VerticalAlignment.Center});
                caps.Children.Add(new Border{Background=SystemColors.ControlBrush,BorderBrush=SystemColors.ActiveBorderBrush,
                    BorderThickness=new Thickness(1,1,1,2),CornerRadius=new CornerRadius(5),Padding=new Thickness(10,4,10,4),
                    Child=new TextBlock{Text=name,FontWeight=FontWeights.SemiBold,Foreground=SystemColors.ControlTextBrush}});
            }
            save.IsEnabled=ValidBinding(m,k);
            status.Text=save.IsEnabled?"":"键盘主键需搭配 Ctrl、Alt 或 Shift。";
            status.Visibility=save.IsEnabled?Visibility.Collapsed:Visibility.Visible;
        };
        Action<int[],int> addRow=(codes,indent)=>{
            var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(indent,0,0,0)};
            rows.Children.Add(row);
            foreach(int code in codes){
                string name=code==-2?"Ctrl":code==-1?"Alt":code==-4?"Shift":KeyName((uint)code);
                var b=new System.Windows.Controls.Primitives.ToggleButton{Content=name,Tag=code};
                if(code<0)b.Width=68;
                if(code==5 || code==6)b.Width=128;
                if(code==116 || code==120 || code==5)b.Margin=new Thickness(12,2,2,2);
                AutomationProperties.SetName(b,name);
                b.Click+=(s,e)=>{
                    if(code<0)m^=(uint)-code;
                    else{k=(uint)code;uint held=(uint)System.Windows.Input.Keyboard.Modifiers;if(held>0 && held<8)m=held;}
                    refresh(0);
                };
                row.Children.Add(b);buttons.Add(b);
            }
        };
        addRow(new[]{112,113,114,115,116,117,118,119,120,121,122,123},0);
        foreach(var line in new[]{"1234567890","QWERTYUIOP","ASDFGHJKL","ZXCVBNM"}){
            addRow(Array.ConvertAll(line.ToCharArray(),c=>(int)c),line[0]=='1'?0:line[0]=='Q'?12:line[0]=='A'?24:44);
        }
        addRow(new[]{-2,-1,-4,5,6},0);
        refresh(0);
        Action<uint,uint> record=(flags,main)=>{m=flags;k=main;refresh(0);};
        w.PreviewKeyDown+=(s,e)=>{
            if(!panel.IsVisible)return;
            var key=e.Key==Key.System?e.SystemKey:e.Key;
            var flags=(uint)System.Windows.Input.Keyboard.Modifiers;
            if(flags>=8 || key==Key.F4 && flags==1)return;
            uint main=(uint)KeyInterop.VirtualKeyFromKey(key);
            if(main>=160 && main<=165){e.Handled=true;refresh(flags);return;}
            if(!IsSide(main) && ValidBinding(7,main)){e.Handled=true;record(flags,main);}
        };
        w.PreviewKeyUp+=(s,e)=>{
            if(!panel.IsVisible)return;
            uint main=(uint)KeyInterop.VirtualKeyFromKey(e.Key==Key.System?e.SystemKey:e.Key);
            if(main>=160 && main<=165){e.Handled=true;refresh((uint)System.Windows.Input.Keyboard.Modifiers);}
        };
        w.PreviewMouseDown+=(s,e)=>{
            if(!panel.IsVisible)return;
            uint side=e.ChangedButton==MouseButton.XButton1?5u:e.ChangedButton==MouseButton.XButton2?6u:0u;
            uint flags=(uint)System.Windows.Input.Keyboard.Modifiers;
            if(side!=0 && flags<8){e.Handled=true;record(flags,side);}
        };
        save.Click+=(s,e)=>{
            if(!ActivateBinding(m,k)){status.Text="组合被占用或无法启用，请换一个。";status.Visibility=Visibility.Visible;return;}
            try{
                Directory.CreateDirectory(Path.GetDirectoryName(config));
                var temp=config+".tmp";File.WriteAllText(temp,m+","+k);
                if(File.Exists(config))File.Replace(temp,config,null);else File.Move(temp,config);
                status.Text="快捷键已保存。";status.Visibility=Visibility.Visible;
            }catch(IOException){MessageBox.Show(w,"快捷键已生效，但无法保存。");}
             catch(UnauthorizedAccessException){MessageBox.Show(w,"快捷键已生效，但无法保存。");}
        };
        settingsWindow=w;setting=true;
        recordCurrentHotkey=()=>{if(w.IsActive && panel.IsVisible)record(mods,vk);};
        try{w.ShowDialog();}finally{settingsWindow=null;recordCurrentHotkey=null;setting=false;}
    }
    Action recordCurrentHotkey;
    bool setting;
    GlassAppearance appearance;
    string AppearancePath { get { return Path.Combine(Path.GetDirectoryName(config),"appearance.xml"); } }
    static string SideName(uint key){return key==5?"鼠标侧键 1（后退）":"鼠标侧键 2（前进）";}
    static uint MouseModifiers(){
        uint m=0;
        if(GetAsyncKeyState(17)<0)m|=2;
        if(GetAsyncKeyState(18)<0)m|=1;
        if(GetAsyncKeyState(16)<0)m|=4;
        if(GetAsyncKeyState(91)<0 || GetAsyncKeyState(92)<0)m|=8;
        return m;
    }
    IntPtr Message(IntPtr w,int msg,IntPtr id,IntPtr lp,ref bool handled){
        if(msg==0x312){handled=true;if(id.ToInt32()==hotkeyId&&keyboardRegistered){if(!setting)BeginSelection();else if(recordCurrentHotkey!=null)recordCurrentHotkey();}else if(id.ToInt32()==2)Dismiss();}return IntPtr.Zero;
    }
    IntPtr Keyboard(int code,IntPtr m,IntPtr d){
        if(code>=0 && copying && (Marshal.ReadInt32(d,8)&16)==0 && (m.ToInt32()==256 || m.ToInt32()==260))activity++;
        return CallNextHookEx(IntPtr.Zero,code,m,d);
    }
    IntPtr Mouse(int code,IntPtr m,IntPtr d){
        int msg=m.ToInt32();
        if(code>=0 && (msg==0x20B || msg==0x20C)){
            uint side=(uint)(Marshal.ReadInt32(d,8)>>16)+4;
            if(msg==0x20B && !setting && side==vk && IsSide(side) && MouseModifiers()==mods){
                pressedSide=side;return new IntPtr(1);
            }
            if(msg==0x20C && pressedSide==side){
                pressedSide=0;
                if(!setting && side==vk)Dispatcher.BeginInvoke(new Action(BeginSelection));
                return new IntPtr(1);
            }
        }
        if(code>=0 && (msg==513 || msg==516 || msg==519 || msg==523)){
            if(copying)activity++;
            if(card!=null && card.IsVisible){WinRect r;int x=Marshal.ReadInt32(d),y=Marshal.ReadInt32(d,4);
                if(GetWindowRect(new WindowInteropHelper(card).Handle,out r) && (x<r.left || x>=r.right || y<r.top || y>=r.bottom))Dismiss();}
        }return CallNextHookEx(IntPtr.Zero,code,m,d);
    }
    void UpdateMouse(){if(IsSide(vk) || copying || card!=null&&card.IsVisible){if(mouseHook==IntPtr.Zero)mouseHook=SetWindowsHookEx(14,mouseFn,GetModuleHandle(null),0);}else if(mouseHook!=IntPtr.Zero){UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;}}
    void Cancel(){if(current!=null){current.Cancel();current.Dispose();current=null;}}
    void Dismiss(){Cancel();source=answer=null;if(card!=null){card.Hide();body.Text="";}UnregisterHotKey(sink.Handle,2);UpdateMouse();}
    async void BeginSelection(){
        var original=Selection.GetForegroundWindow();Dismiss();
        var cursor=F.Cursor.Position;anchor=new Point(cursor.X,cursor.Y+12);
        selectionTop=cursor.Y-12;
        current=new CancellationTokenSource();var token=current.Token;
        try{
            await capture.WaitAsync(token);
            Selection value;
            try{
                var selectionPoint=new Point(cursor.X,cursor.Y);
                var direct=Task.Run(()=>Selection.ReadDirect(original,selectionPoint));
                value=await Task.WhenAny(direct,Task.Delay(650,token))==direct?await direct:null;
                token.ThrowIfCancellationRequested();
                if(original!=Selection.GetForegroundWindow())return;
                if(value==null){
                    copying=true;UpdateMouse();keyHook=SetWindowsHookEx(13,keyFn,GetModuleHandle(null),0);
                    try{if(keyHook!=IntPtr.Zero && mouseHook!=IntPtr.Zero)value=await Selection.CopyFallback(original,sink.Handle,()=>activity,token);}
                    finally{if(keyHook!=IntPtr.Zero)UnhookWindowsHookEx(keyHook);keyHook=IntPtr.Zero;copying=false;UpdateMouse();}
                }
            }finally{capture.Release();}
            token.ThrowIfCancellationRequested();
            if(value==null || string.IsNullOrWhiteSpace(value.Text)){Display("未获取到选中文字，请重新选择。");return;}
            if(value.Text.Length>4000){Display("选区较长，请分段选择（每次最多 4000 个字符）。");return;}
            if(!value.Bounds.IsEmpty){
                anchor=new Point(value.Bounds.Left+Math.Min(26,value.Bounds.Width/2),value.Bounds.Bottom+6);
                selectionTop=value.Bounds.Top-6;
            }
            source=value.Text;target=LocalTranslator.Target(source);RunTranslation();
        }catch(OperationCanceledException){}catch(Exception ex){if(!token.IsCancellationRequested)Display("取词失败："+ex.Message);}
    }
    async void RunTranslation(){
        if(source==null)return;Cancel();current=new CancellationTokenSource();var token=current.Token;
        answer=null;Display("翻译中…");
        try{
            var translated=await translator.Translate(source,target,s=>{if(!token.IsCancellationRequested)Display(s);},token);
            if(token.IsCancellationRequested)return;answer=translated;Display(answer);
        }catch(Exception ex){if(!token.IsCancellationRequested){Display("翻译失败。"+ex.Message);retry.Visibility=Visibility.Visible;}}
    }
    Button Action(string label,Action click){var b=new Button{Content=label};b.Click+=(s,e)=>click();return b;}
    void BuildCard(){
        card=new Window{Title="Glossa",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowActivated=false,ShowInTaskbar=false,Topmost=true,SizeToContent=SizeToContent.Height,Width=350,AllowsTransparency=true,Background=Brushes.Transparent};
        DockPanel layout;
        using(var stream=typeof(App).Assembly.GetManifestResourceStream("Popup.xaml"))
            layout=(DockPanel)System.Windows.Markup.XamlReader.Load(stream);
        card.Content=layout;
        tip=(System.Windows.Shapes.Path)layout.FindName("Tip");
        body=(TextBlock)layout.FindName("Body");
        scroll=(ScrollViewer)layout.FindName("Scroll");
        var row=(WrapPanel)layout.FindName("Actions");
        if(SystemParameters.HighContrast){
            var surface=(Border)layout.FindName("Surface");
            surface.Background=SystemColors.WindowBrush;
            surface.BorderBrush=tip.Stroke=SystemColors.WindowTextBrush;
            tip.Fill=SystemColors.WindowBrush;
            body.Foreground=SystemColors.WindowTextBrush;
            layout.Resources.Clear();
        }
        language=Action("",()=>{target=target=="中文"?"英文":"中文";RunTranslation();});
        retry=Action("重试",RunTranslation);copy=Action("复制",Copy);
        row.Children.Add(language);row.Children.Add(copy);row.Children.Add(retry);
        card.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape){e.Handled=true;Dismiss();}};
        card.SizeChanged+=(s,e)=>Place();
        if(!SystemParameters.HighContrast){
            var style=appearance ?? new GlassAppearance();
            var surface=(Border)layout.FindName("Surface");
            style.Apply(surface,tip,body);
            if(style.Blur)PopupGlass.Attach(card,surface);
        }
    }
    async void Copy(){if(answer==null)return;var text=answer;var request=current;try{Clipboard.SetDataObject(text,true);copy.Content="已复制";}catch(ExternalException){copy.Content="复制失败，重试";return;}
        await Task.Delay(1200);if(current==request)copy.Content="复制";}
    void Display(string message){
        if(card==null)BuildCard();body.Text=message;scroll.ScrollToTop();
        language.Content=target=="英文"?"译成中文":"译成英文";language.Visibility=source==null?Visibility.Collapsed:Visibility.Visible;
        retry.Visibility=Visibility.Collapsed;copy.Visibility=answer==null?Visibility.Collapsed:Visibility.Visible;copy.Content="复制";
        if(!card.IsVisible){card.Show();RegisterHotKey(sink.Handle,2,0x4000,27);UpdateMouse();}card.UpdateLayout();Place();
    }
    void Place(){if(card==null || !card.IsVisible)return;var src=PresentationSource.FromVisual(card);if(src==null || src.CompositionTarget==null)return;
        var area=F.Screen.FromPoint(new System.Drawing.Point((int)anchor.X,(int)anchor.Y)).WorkingArea;
        var transform=src.CompositionTarget.TransformFromDevice;
        var p=transform.Transform(anchor);var a=transform.Transform(new Point(area.Left,area.Top));var z=transform.Transform(new Point(area.Right,area.Bottom));
        card.Left=Math.Max(a.X,Math.Min(p.X-32,z.X-card.ActualWidth));
        double above=transform.Transform(new Point(anchor.X,selectionTop)).Y;
        bool flip=p.Y+card.ActualHeight>z.Y && above-a.Y>z.Y-p.Y;
        card.Top=Math.Max(a.Y,Math.Min(flip?above-card.ActualHeight:p.Y,z.Y-card.ActualHeight));
        DockPanel.SetDock(tip,flip?Dock.Bottom:Dock.Top);
        tip.RenderTransformOrigin=new Point(.5,.5);
        tip.RenderTransform=flip?new ScaleTransform(1,-1):Transform.Identity;
        tip.Margin=new Thickness(Math.Max(12,Math.Min(p.X-card.Left-13,card.ActualWidth-38)),0,0,0);
    }
    protected override void OnExit(ExitEventArgs e){Dismiss();if(mouseHook!=IntPtr.Zero)UnhookWindowsHookEx(mouseHook);if(tray!=null){tray.Visible=false;tray.ContextMenuStrip.Dispose();tray.Dispose();}UnregisterHotKey(sink.Handle,hotkeyId);translator.Dispose();sink.Dispose();base.OnExit(e);}
}

