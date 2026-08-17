// Hl7.Fhir.Model has a Task resource, which collides with System.Threading.Tasks.Task in any
// file that touches both. The alias keeps `Task` meaning what it means in every other test.
global using Task = System.Threading.Tasks.Task;
