using Newtonsoft.Json.Schema.Generation;
using BirthdayBot.Config;

var gen = new JSchemaGenerator();
var sch = gen.Generate(typeof(Configuration));
Console.WriteLine(sch.ToString());
