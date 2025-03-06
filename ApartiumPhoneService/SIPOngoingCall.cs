using SIPSorcery.SIP.App;

namespace ApartiumPhoneService;

public class SIPOngoingCall(SIPUserAgent userAgent, SIPServerUserAgent serverUserAgent)
{
    public void Hangup()
    {
        userAgent.Hangup();
        serverUserAgent.Hangup(true);
    }
}