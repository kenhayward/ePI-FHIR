// FHIR defines a resource type called Task, which collides with the framework type in any
// file doing both FHIR and async work. Alias it once.
global using Task = System.Threading.Tasks.Task;
