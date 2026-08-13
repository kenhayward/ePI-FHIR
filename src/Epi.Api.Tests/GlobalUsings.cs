// FHIR defines resource types called Task and Claim, which collide with the framework types
// of the same name in any file doing both FHIR and web work. Alias them once.
global using Task = System.Threading.Tasks.Task;
global using Claim = System.Security.Claims.Claim;
