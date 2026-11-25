using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Xml.Serialization;
using System.Windows.Markup;


public class GlassAppearance {
    public int Rgb = 0xE6F1F7;
    public int Transparency = 26;
    public bool Blur = true;
    public int Gradient = 35;
    public int Highlight = 90;
    public int Radius = 12;
    public int Shadow = 22;

    internal static GlassAppearance Load(string path){
        if(File.Exists(path)){
            try{
                using(var f=File.OpenRead(path)){
                    var a=(GlassAppearance)new XmlSerializer(typeof(GlassAppearance)).Deserialize(f);
                    if(a!=null && a.Rgb>=0 && a.Rgb<=0xffffff && Percent(a.Transparency) && Percent(a.Gradient)
                       && Percent(a.Highlight) && a.Radius>=0 && a.Radius<=24 && Percent(a.Shadow))return a;
                }
            }catch(IOException){}catch(InvalidOperationException){}catch(UnauthorizedAccessException){}
        }
        return new GlassAppearance();
    }
    static bool Percent(int n){return n>=0 && n<=100;}
    internal void Save(string path){
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp=path+".tmp";
        using(var f=File.Create(temp))new XmlSerializer(typeof(GlassAppearance)).Serialize(f,this);
        if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
    }

    static Color Mix(Color color, Color target,double strength){
        return Color.FromRgb((byte)Math.Round(color.R+(target.R-color.R)*strength),
                             (byte)Math.Round(color.G+(target.G-color.G)*strength),
                             (byte)Math.Round(color.B+(target.B-color.B)*strength));
    }
    internal void Apply(Border face,System.Windows.Shapes.Path tip,TextBlock body){
        Color color=Color.FromRgb((byte)(Rgb>>16),(byte)(Rgb>>8),(byte)Rgb);
        var fill=((LinearGradientBrush)face.Background).Clone();
        fill.GradientStops[0].Color=Mix(color,Colors.White,Gradient/100.0);
        fill.GradientStops[1].Color=color;
        fill.GradientStops[2].Color=Mix(color,Colors.Black,Gradient/800.0);
        face.Background=fill;
        fill.Opacity=1-Transparency/100.0;
        tip.Fill=new SolidColorBrush(fill.GradientStops[0].Color){Opacity=fill.Opacity};
        var edge=face.BorderBrush.Clone();
        edge.Opacity=Highlight/100.0;
        face.BorderBrush=tip.Stroke=edge;
        face.CornerRadius=new CornerRadius(Radius);
        var shadow=((DropShadowEffect)face.Effect).Clone();
        shadow.Opacity=Shadow/100.0;
        face.Effect=shadow;
        if((color.R*0.299+color.G*0.587+color.B*0.114)<128 && Transparency<50)
            body.Foreground=Brushes.White;
        else body.ClearValue(TextBlock.ForegroundProperty);
    }

    static Slider AddSlider(StackPanel panel,string name,int value,int max,string unit){
        var row=new DockPanel{Margin=new Thickness(0,3,0,3)};
        var label=new Label{Content=name,Width=78,Padding=new Thickness(0),VerticalAlignment=VerticalAlignment.Center};
        var number=new TextBlock{Width=43,TextAlignment=TextAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
        DockPanel.SetDock(number,Dock.Right);
        row.Children.Add(label);row.Children.Add(number);
        var slider=new Slider{Minimum=0,Maximum=max,Value=value,IsSnapToTickEnabled=true,TickFrequency=1,
            IsMoveToPointEnabled=true,VerticalAlignment=VerticalAlignment.Center};
        AutomationProperties.SetName(slider,name);
        label.Target=slider;
        Action refresh=()=>number.Text=(int)slider.Value+unit;
        refresh();slider.ValueChanged+=(s,e)=>refresh();
        row.Children.Add(slider);panel.Children.Add(row);
        return slider;
    }
    internal DockPanel CreatePage(Window w,string path,Action<GlassAppearance> saved){
        var draft=(GlassAppearance)MemberwiseClone();
        DockPanel panel,sample;
        using(var stream=typeof(App).Assembly.GetManifestResourceStream("Preview.xaml"))panel=(DockPanel)XamlReader.Load(stream);
        using(var stream=typeof(App).Assembly.GetManifestResourceStream("Popup.xaml"))sample=(DockPanel)XamlReader.Load(stream);
        sample.MaxWidth=350;
        sample.Margin=new Thickness(20,6,20,12);
        var slot=(Grid)panel.FindName("PreviewSlot");
        slot.Children.Add(sample);
        var face=(Border)sample.FindName("Surface");
        // Keep the panel opaque around the live desktop sample, including its rounded corners.
        var mat=(Border)panel.FindName("PreviewMat");
        var outer=new RectangleGeometry();
        var cutout=new RectangleGeometry();
        mat.Clip=new CombinedGeometry(GeometryCombineMode.Exclude,outer,cutout);
        Action shapePreview=()=>{
            if(!face.IsVisible || !face.IsArrangeValid)return;
            var area=new Rect(mat.RenderSize);
            var glass=new Rect(face.TranslatePoint(new Point(),mat),face.RenderSize);
            if(outer.Rect!=area)outer.Rect=area;
            if(cutout.Rect!=glass)cutout.Rect=glass;
            if(cutout.RadiusX!=face.CornerRadius.TopLeft)
                cutout.RadiusX=cutout.RadiusY=face.CornerRadius.TopLeft;
        };
        slot.LayoutUpdated+=(s,e)=>shapePreview();
        var tip=(System.Windows.Shapes.Path)sample.FindName("Tip");
        tip.Margin=new Thickness(20,0,0,0);
        var body=(TextBlock)sample.FindName("Body");
        body.Text="这是一段示例译文。";
        sample.ToolTip="拖动可更换预览位置";
        sample.MouseLeftButtonDown+=(s,e)=>w.DragMove();
        var basic=(StackPanel)panel.FindName("Basic");
        var advanced=(StackPanel)panel.FindName("Advanced");
        var save=(Button)panel.FindName("SaveButton");
        var align=PopupGlass.Attach(w,face,()=>draft.Blur && face.IsVisible);
        var colors=new StackPanel();
        var swatch=new Border{Width=20,Height=14,Margin=new Thickness(0,0,6,0),BorderThickness=new Thickness(1),BorderBrush=Brushes.Gray};
        var colorText=new TextBlock();
        var colorHeader=new StackPanel{Orientation=Orientation.Horizontal};
        colorHeader.Children.Add(swatch);colorHeader.Children.Add(colorText);
        basic.Children.Add(new Expander{Header=colorHeader,Content=colors});
        var red=AddSlider(colors,"红",(Rgb>>16)&255,255,"");
        var green=AddSlider(colors,"绿",(Rgb>>8)&255,255,"");
        var blue=AddSlider(colors,"蓝",Rgb&255,255,"");
        var picker=new Button{Content="更多颜色…",HorizontalAlignment=HorizontalAlignment.Left};
        colors.Children.Add(picker);
        picker.Click+=(s,e)=>{
            using(var dialog=new System.Windows.Forms.ColorDialog{FullOpen=true,Color=System.Drawing.Color.FromArgb((int)red.Value,(int)green.Value,(int)blue.Value)}){
                var owner=new System.Windows.Forms.NativeWindow();
                owner.AssignHandle(new System.Windows.Interop.WindowInteropHelper(w).Handle);
                try{if(dialog.ShowDialog(owner)==System.Windows.Forms.DialogResult.OK){
                    red.Value=dialog.Color.R;green.Value=dialog.Color.G;blue.Value=dialog.Color.B;
                }}finally{owner.ReleaseHandle();}
            }
        };
        var transparency=AddSlider(basic,"玻璃透明度",Transparency,100,"%");
        var blur=new CheckBox{Content="背景模糊",IsChecked=Blur,Margin=new Thickness(0,6,0,0)};
        basic.Children.Add(blur);
        var gradient=AddSlider(advanced,"渐变强度",Gradient,100,"%");
        var highlight=AddSlider(advanced,"边缘高光",Highlight,100,"%");
        var radius=AddSlider(advanced,"圆角大小",Radius,24," DIP");
        var shadow=AddSlider(advanced,"阴影强度",Shadow,100,"%");
        var status=(TextBlock)panel.FindName("Status");
        Action refreshPreview=()=>{
            status.Text="";
            draft.Rgb=((int)red.Value<<16)|((int)green.Value<<8)|(int)blue.Value;
            colorText.Text="玻璃颜色    #"+draft.Rgb.ToString("X6");
            swatch.Background=new SolidColorBrush(Color.FromRgb((byte)red.Value,(byte)green.Value,(byte)blue.Value));
            draft.Transparency=(int)transparency.Value;draft.Blur=blur.IsChecked==true;
            draft.Gradient=(int)gradient.Value;draft.Highlight=(int)highlight.Value;
            draft.Radius=(int)radius.Value;draft.Shadow=(int)shadow.Value;
            if(SystemParameters.HighContrast){
                face.Background=tip.Fill=SystemColors.WindowBrush;
                face.BorderBrush=tip.Stroke=body.Foreground=SystemColors.WindowTextBrush;

            }else{
                draft.Apply(face,tip,body);
                shapePreview();
                if(align!=null)align();
            }
        };
        foreach(var slider in new[]{red,green,blue,transparency,gradient,highlight,radius,shadow})
            slider.ValueChanged+=(s,e)=>refreshPreview();
        blur.Checked+=(s,e)=>refreshPreview();blur.Unchecked+=(s,e)=>refreshPreview();
        refreshPreview();
        ((Button)panel.FindName("ResetButton")).Click+=(s,e)=>{
            var defaults=new GlassAppearance();
            red.Value=(defaults.Rgb>>16)&255;
            green.Value=(defaults.Rgb>>8)&255;
            blue.Value=defaults.Rgb&255;
            transparency.Value=defaults.Transparency;
            blur.IsChecked=defaults.Blur;
            gradient.Value=defaults.Gradient;
            highlight.Value=defaults.Highlight;
            radius.Value=defaults.Radius;
            shadow.Value=defaults.Shadow;
        };
        save.Click+=(s,e)=>{
            try{draft.Save(path);saved((GlassAppearance)draft.MemberwiseClone());status.Text="外观已保存。";}
            catch(IOException ex){MessageBox.Show(w,ex.Message,"无法保存外观");}
            catch(UnauthorizedAccessException ex){MessageBox.Show(w,ex.Message,"无法保存外观");}
        };
        return panel;
    }
}
