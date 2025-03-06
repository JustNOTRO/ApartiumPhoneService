using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace ApartiumPhoneService;

/// <summary>
/// <c>SIPRequestHandler</c> Handles a SIP request
/// </summary>
/// <param name="server">the SIP server</param>
/// <param name="sipRequest">the SIP request</param>
/// <param name="sipEndPoint">the SIP endpoint</param>
/// <param name="remoteEndPoint">the remote end point</param>
public class SIPRequestHandler(
    ApartiumPhoneServer server,
    SIPRequest sipRequest,
    SIPEndPoint sipEndPoint,
    SIPEndPoint remoteEndPoint)
{
    /// <summary>
    /// Handles requests by method type
    /// </summary>
    public async Task Handle()
    {
        var sipTransport = server.GetSipTransport();

        switch (sipRequest.Method)
        {
            case SIPMethodsEnum.INVITE:
            {
                await HandleIncomingCall(sipTransport);
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

    /// <summary>
    /// Handles incoming call
    /// </summary>
    /// <param name="sipTransport">the server SIP transport</param>
    private async Task HandleIncomingCall(SIPTransport sipTransport)
    {
        var logger = ApartiumPhoneServer.GetLogger();
        logger.LogInformation($"Incoming call request: {sipEndPoint}<-{remoteEndPoint} {sipRequest.URI}.");

        var sipUserAgent = new SIPUserAgent(sipTransport, null);
        sipUserAgent.OnCallHungup += OnHangup;
        sipUserAgent.ServerCallCancelled += (_, _) => logger.LogDebug("Incoming call cancelled by remote party.");

        sipUserAgent.OnDtmfTone += (key, duration) => OnDtmfTone(sipUserAgent, key, duration);
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
        var rtpSession = CreateRtpSession(sipUserAgent, sipRequest.URI.User);

        // Insert a brief delay to allow testing of the "Ringing" progress response.
        // Without the delay the call gets answered before it can be sent.
        await Task.Delay(500);

        await sipUserAgent.Answer(serverUserAgent, rtpSession);

        if (sipUserAgent.IsCallActive)
        {
            await rtpSession.Start();
            if (!server.TryAddCall(sipUserAgent.Dialogue.CallId, 
                    new SIPOngoingCall(sipUserAgent, serverUserAgent)))
            {
                logger.LogWarning("Could not add call to active calls");
            }
        }
    }
    
    /// <summary>
    /// Handles the call when client hangup
    /// </summary>
    /// <param name="dialogue">The call dialogue</param>
    private void OnHangup(SIPDialogue? dialogue)
    {
        if (dialogue == null)
        {
            return;
        }

        var callId = dialogue.CallId;
        var call = server.TryRemoveCall(callId);

        // This app only uses each SIP user agent once so here the agent is 
        // explicitly closed to prevent is responding to any new SIP requests.
        call?.Hangup();
    }
    
    /// <summary>
    /// Handles DTMF tones received from client
    /// </summary>
    /// <param name="userAgent">The user agent of the client</param>
    /// <param name="key">The key that was pressed</param>
    /// <param name="duration">The duration of the press</param>
    private void OnDtmfTone(SIPUserAgent userAgent, byte key, int duration)
    {
        var logger = ApartiumPhoneServer.GetLogger();
        var callId = userAgent.Dialogue.CallId;
        logger.LogInformation($"Call {callId} received DTMF tone {key}, duration {duration}ms.");

        var keyPressed = key switch
        {
            10 => '*',
            11 => '#',
            _ => key.ToString()[0]
        };

        Console.WriteLine("User pressed {0}!", keyPressed);
    }
    
    /// <summary>
    /// Creates RTP session
    /// </summary>
    /// <param name="userAgent">The client user agent</param>
    /// <param name="destination">The destination of the audio sources</param>
    /// <returns>A new VoIP media session</returns>
    private VoIPMediaSession CreateRtpSession(SIPUserAgent userAgent, string destination)
    {
        var logger = ApartiumPhoneServer.GetLogger();
        var codecs = new List<AudioCodecsEnum> { AudioCodecsEnum.PCMU, AudioCodecsEnum.PCMA, AudioCodecsEnum.G722 };

        if (string.IsNullOrEmpty(destination) || !Enum.TryParse(destination, out AudioSourcesEnum audioSource))
        {
            audioSource = AudioSourcesEnum.Music;
        }

        logger.LogInformation($"RTP audio session source set to {audioSource}.");

        var audioExtrasSource =
            new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = audioSource });
        audioExtrasSource.RestrictFormats(formats => codecs.Contains(formats.Codec));

        var rtpAudioSession = new VoIPMediaSession(new MediaEndPoints { AudioSource = audioExtrasSource });
        rtpAudioSession.AcceptRtpFromAny = true;

        rtpAudioSession.OnTimeout += _ =>
        {
            logger.LogWarning(userAgent.Dialogue != null
                ? $"RTP timeout on call with {userAgent.Dialogue.RemoteTarget}, hanging up."
                : $"RTP timeout on incomplete call, closing RTP session.");

            userAgent.Hangup();
        };

        return rtpAudioSession;
    }
}