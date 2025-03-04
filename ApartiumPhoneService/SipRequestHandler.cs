using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace ApartiumPhoneService;

public class SipRequestHandler(
    ApartiumPhoneServer server,
    SIPRequest sipRequest,
    SIPEndPoint sipEndPoint,
    SIPEndPoint remoteEndPoint)
{
    public async Task Handle()
    {
        var sipTransport = server.GetSipTransport();

        switch (sipRequest.Method)
        {
            case SIPMethodsEnum.INVITE:
            {
                await HandleClientInvitation(sipTransport);
                break;
            }

            case SIPMethodsEnum.BYE:
            {
                var byeResponse = SIPResponse.GetResponse(sipRequest,
                    SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                await sipTransport.SendResponseAsync(byeResponse);
                break;
            }

            case SIPMethodsEnum.SUBSCRIBE:
            {
                var notAllowedResponse =
                    SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                await sipTransport.SendResponseAsync(notAllowedResponse);
                break;
            }

            case SIPMethodsEnum.OPTIONS:
            case SIPMethodsEnum.REGISTER:
            {
                var optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                await sipTransport.SendResponseAsync(optionsResponse);
                break;
            }
        }
    }

    private async Task HandleClientInvitation(SIPTransport sipTransport)
    {
        var logger = ApartiumPhoneServer.GetLogger();
        logger.LogInformation($"Incoming call request: {sipEndPoint}<-{remoteEndPoint} {sipRequest.URI}.");

        var sipUserAgent = new SIPUserAgent(sipTransport, null);
        sipUserAgent.OnCallHungup += server.OnHangup;
        sipUserAgent.ServerCallCancelled += (_, _) => logger.LogDebug("Incoming call cancelled by remote party.");

        sipUserAgent.OnDtmfTone += (key, duration) => server.OnDtmfTone(sipUserAgent, key, duration);
        sipUserAgent.OnRtpEvent += (evt, hdr) =>
            logger.LogDebug(
                $"rtp event {evt.EventID}, duration {evt.Duration}, end of event {evt.EndOfEvent}, timestamp {hdr.Timestamp}, marker {hdr.MarkerBit}.");

        //ua.OnTransactionTraceMessage += (tx, msg) => Log.LogDebug($"uas tx {tx.TransactionId}: {msg}");
        sipUserAgent.ServerCallRingTimeout += serverUserAgent =>
        {
            var transactionState = serverUserAgent.ClientTransaction.TransactionState;
            logger.LogWarning(
                $"Incoming call timed out in {transactionState} state waiting for client ACK, terminating.");
            sipUserAgent.Hangup();
        };

        var serverUserAgent = sipUserAgent.AcceptCall(sipRequest);
        var rtpSession = server.CreateRtpSession(sipUserAgent, sipRequest.URI.User);

        // Insert a brief delay to allow testing of the "Ringing" progress response.
        // Without the delay the call gets answered before it can be sent.
        await Task.Delay(500);

        await sipUserAgent.Answer(serverUserAgent, rtpSession);

        if (sipUserAgent.IsCallActive)
        {
            await rtpSession.Start();
            server.TryAddCall(sipUserAgent.Dialogue.CallId, sipUserAgent);
        }
    }
}