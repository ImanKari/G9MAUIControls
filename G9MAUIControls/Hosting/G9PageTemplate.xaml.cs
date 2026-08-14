namespace G9MAUIControls.Hosting;

/// <summary>
///     The suite's page control template, as a mergeable resource dictionary.
///     <para>
///         <b>Every consumer must merge this.</b> <see cref="G9PageBase" /> resolves the template by the
///         resource key <c>G9PageTemplate</c> in its constructor and throws when it is absent, because the
///         six-layer z-stack the template declares is what popup, bottom sheet, toast, and the progress
///         overlay resolve their host through. Merge it into <c>App.xaml</c> before any page is constructed:
///     </para>
///     <code>
///     &lt;Application
///         xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
///         xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
///         xmlns:g9="clr-namespace:G9MAUIControls.Hosting;assembly=G9MAUIControls"
///         xmlns:g9Theme="clr-namespace:G9MAUIControls.Theming;assembly=G9MAUIControls"&gt;
///         &lt;Application.Resources&gt;
///             &lt;ResourceDictionary&gt;
///                 &lt;ResourceDictionary.MergedDictionaries&gt;
///                     &lt;g9:G9PageTemplate /&gt;
///                     &lt;g9Theme:G9ThemeLight /&gt;
///                 &lt;/ResourceDictionary.MergedDictionaries&gt;
///             &lt;/ResourceDictionary&gt;
///         &lt;/Application.Resources&gt;
///     &lt;/Application&gt;
///     </code>
///     <para>
///         Merge by <b>type</b>, as above — not by <c>Source="/Hosting/G9PageTemplate.xaml"</c>. A source
///         path resolves only inside the assembly that declares it, so the path form fails a consumer's
///         Release build with XC0124. See LES-0013.
///     </para>
/// </summary>
public partial class G9PageTemplate : ResourceDictionary
{
    /// <summary>Creates the dictionary. XAML calls this; application code merges the type.</summary>
    public G9PageTemplate() => InitializeComponent();
}
