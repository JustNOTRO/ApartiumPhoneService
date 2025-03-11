using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace ApartiumPhoneService;

public class SIPUserAgentFactory : ISIPUserAgentFactory
{
    public SIPUserAgentWrapper Create(SIPTransport sipTransport, SIPEndPoint outboundProxy)
    {
        return new SIPUserAgentWrapper(sipTransport, outboundProxy);
    }
}