using SIPSorcery.SIP.App;

namespace ApartiumPhoneService;

public class SIPOngoingCall(SIPUserAgent userAgent, SIPServerUserAgent serverUserAgent, VoIpAudioPlayer voIpAudioPlayer)
{
    public virtual void Hangup()
    {
        userAgent.Hangup();
        serverUserAgent.Hangup(true);
    }
}