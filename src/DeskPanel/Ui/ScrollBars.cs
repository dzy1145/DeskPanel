using System.Windows;
using System.Windows.Markup;

namespace DeskPanel.Ui;

/// <summary>全局细滚动条：6px、透明槽、圆角半透明滑块。避免系统那条白色大滚动条。</summary>
internal static class ScrollBars
{
    private const string Xaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
       TargetType="{x:Type ScrollBar}">
  <Setter Property="Width" Value="6"/>
  <Setter Property="MinWidth" Value="6"/>
  <Setter Property="Background" Value="Transparent"/>
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="{x:Type ScrollBar}">
        <Grid Background="Transparent">
          <Track x:Name="PART_Track" IsDirectionReversed="True">
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType="{x:Type Thumb}">
                    <Border CornerRadius="3" Background="#2EFFFFFF"/>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
""";

    public static void Apply(Application app)
    {
        try
        {
            var style = (Style)XamlReader.Parse(Xaml);
            app.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = style;
        }
        catch { }
    }
}
