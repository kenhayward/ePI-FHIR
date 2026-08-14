// Hl7.Fhir.Model.Task is a FHIR resource type and collides with the framework's Task in any
// file that does both FHIR and async work. Alias it once here rather than fully qualifying
// at every use.
global using Task = System.Threading.Tasks.Task;
