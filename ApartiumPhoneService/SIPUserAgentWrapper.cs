using System.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace ApartiumPhoneService;

/// <summary>
/// UserAgent wrapper class for mocking the behavior of UserAgent
/// </summary>
/// <param name="sipTransport">The sip transport that manages the sip channels</param>
/// <param name="outboundProxy">The outbound proxy</param>
public class SIPUserAgentWrapper(SIPTransport sipTransport, SIPEndPoint outboundProxy)
    : SIPUserAgent(sipTransport, outboundProxy)
{
    public new virtual SIPServerUserAgent AcceptCall(SIPRequest sipRequest)
    {
        return base.AcceptCall(sipRequest);
    }

    public new virtual Task<bool> Answer(SIPServerUserAgent serverUserAgent, IMediaSession mediaSession,
        IPAddress ipAddress)
    {
        return base.Answer(serverUserAgent, mediaSession, ipAddress);
    }

    public new virtual SIPDialogue Dialogue()
    {
        return base.Dialogue;
    }

    public new virtual void Hangup()
    {
        base.Hangup();
    }

    public new virtual bool IsCallActive => base.IsCallActive;
    
}