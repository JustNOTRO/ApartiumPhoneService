using ApartiumPhoneService;

Console.WriteLine("Starting ApartiumPhoneService.. Hold tight!");
var serverFilePath = $"{Directory.GetCurrentDirectory()}/Data/server.yml";
var server = new ApartiumPhoneServer(serverFilePath);
server.Start();