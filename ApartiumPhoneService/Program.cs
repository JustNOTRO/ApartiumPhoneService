using ApartiumPhoneService;

Console.WriteLine("Starting ApartiumPhoneService.. Hold tight!");
var directory = Directory.GetCurrentDirectory();

var configFilePath = $"{directory}/server.yml";
var server = new ApartiumPhoneServer(configFilePath);
server.Start();