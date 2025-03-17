using SIPSorcery.SIP;

namespace ApartiumPhoneService;

/// <summary>
/// UserAgent factory for creating new user agents
/// </summary>
public interface ISIPUserAgentFactory
{
    SIPUserAgentWrapper Create(SIPTransport sipTransport, SIPEndPoint outboundProxy);
}