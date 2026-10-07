namespace scada_demo_test.Maui.Controls;

public partial class StatusBanner : ContentView
{
    public static readonly BindableProperty StatusTextProperty =
        BindableProperty.Create(nameof(StatusText), typeof(string), typeof(StatusBanner), "Online", propertyChanged: OnStatusChanged);

    public static readonly BindableProperty SubtitleTextProperty =
        BindableProperty.Create(nameof(SubtitleText), typeof(string), typeof(StatusBanner), string.Empty, propertyChanged: OnSubtitleChanged);

    public static readonly BindableProperty BadgeColorProperty =
        BindableProperty.Create(nameof(BadgeColor), typeof(string), typeof(StatusBanner), "#10B981", propertyChanged: OnBadgeColorChanged);

    public static readonly BindableProperty IsFallbackProperty =
        BindableProperty.Create(nameof(IsFallback), typeof(bool), typeof(StatusBanner), false, propertyChanged: OnIsFallbackChanged);

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public string SubtitleText
    {
        get => (string)GetValue(SubtitleTextProperty);
        set => SetValue(SubtitleTextProperty, value);
    }

    public string BadgeColor
    {
        get => (string)GetValue(BadgeColorProperty);
        set => SetValue(BadgeColorProperty, value);
    }

    public bool IsFallback
    {
        get => (bool)GetValue(IsFallbackProperty);
        set => SetValue(IsFallbackProperty, value);
    }

    public StatusBanner()
    {
        InitializeComponent();
    }

    private static void OnStatusChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is StatusBanner banner && newValue is string text)
        {
            banner.StatusLabel.Text = text;
        }
    }

    private static void OnSubtitleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is StatusBanner banner && newValue is string text)
        {
            banner.SubtitleLabel.Text = text;
            banner.SubtitleLabel.IsVisible = !string.IsNullOrEmpty(text);
        }
    }

    private static void OnBadgeColorChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is StatusBanner banner && newValue is string colorStr)
        {
            if (Color.FromArgb(colorStr) is Color parsed)
            {
                banner.DotIndicator.Color = parsed;
                banner.BadgeLabel.TextColor = parsed;
            }
        }
    }

    private static void OnIsFallbackChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is StatusBanner banner && newValue is bool isFallback)
        {
            banner.BadgeLabel.Text = isFallback ? "FALLBACK SIM" : "SIMULATION";
        }
    }
}
