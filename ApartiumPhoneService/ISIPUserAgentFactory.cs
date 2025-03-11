using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace ApartiumPhoneService;

public interface ISIPUserAgentFactory
{
    SIPUserAgentWrapper Create(SIPTransport sipTransport, SIPEndPoint outboundProxy);
}