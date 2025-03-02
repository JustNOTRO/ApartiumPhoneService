using ApartiumPhoneService;
using SIPSorcery.SIP;

List<ApartiumPhoneServer.SipRegisterAccount> sipAccounts = [new("500", "123", "localhost", 120)];
        
Console.WriteLine("Starting ApartiumPhoneService.. Hold tight!");
var server = new ApartiumPhoneServer(sipAccounts, new SIPTransport());
server.Start();