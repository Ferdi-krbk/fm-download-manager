namespace McDm.Tests;

internal sealed class UiCultureScope : IDisposable
{
    private readonly System.Globalization.CultureInfo _previous = System.Globalization.CultureInfo.CurrentUICulture;
    public UiCultureScope(string name) => System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(name);
    public void Dispose() => System.Globalization.CultureInfo.CurrentUICulture = _previous;
}
