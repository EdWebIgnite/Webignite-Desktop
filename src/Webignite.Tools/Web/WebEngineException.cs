namespace Webignite.Tools.Web;

/// <summary>The browser couldn't start or carry on. The message is for the person.</summary>
public sealed class WebEngineException : Exception
{
    public WebEngineException()
    {
    }

    public WebEngineException(string message) : base(message)
    {
    }

    public WebEngineException(string message, Exception inner) : base(message, inner)
    {
    }

    /// <summary>True when the PC has no WebView2, so the tool can say where to get it.</summary>
    public bool Missing { get; init; }
}
