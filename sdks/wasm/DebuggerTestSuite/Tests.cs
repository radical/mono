using System;
using System.Linq;
using System.Threading.Tasks;

using System.Net.WebSockets;
using System.Threading;
using System.IO;
using System.Text;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;
using Xunit;
using WebAssembly.Net.Debugging;

namespace DebuggerTests
{

	public class SourceList : DebuggerTestBase {
		DebugTestContext ctx;
		Dictionary<string, string> dicScriptsIdToUrl;
		Dictionary<string, string> dicFileToUrl;
		Dictionary<string, string> SubscribeToScripts (Inspector insp) {
			dicScriptsIdToUrl = new Dictionary<string, string> ();
			dicFileToUrl = new Dictionary<string, string>();
			insp.On("Debugger.scriptParsed", async (args, c) => {
				var script_id = args? ["scriptId"]?.Value<string> ();
				var url = args["url"]?.Value<string> ();
				if (script_id.StartsWith("dotnet://"))
				{
					var dbgUrl = args["dotNetUrl"]?.Value<string>();
					var arrStr = dbgUrl.Split("/");
					dbgUrl = arrStr[0] + "/" + arrStr[1] + "/" + arrStr[2] + "/" + arrStr[arrStr.Length - 1];
					dicScriptsIdToUrl[script_id] = dbgUrl;
					dicFileToUrl[dbgUrl] = args["url"]?.Value<string>();
				} else if (!String.IsNullOrEmpty (url)) {
					dicFileToUrl[new Uri (url).AbsolutePath] = url;
				}
				await Task.FromResult (0);
			});
			return dicScriptsIdToUrl;
		}

		void CheckLocation (string script_loc, int line, int column, Dictionary<string, string> scripts, JToken location)
		{
			var loc_str = $"{ scripts[location["scriptId"].Value<string>()] }"
							+ $"#{ location ["lineNumber"].Value<int> () }"
							+ $"#{ location ["columnNumber"].Value<int> () }";

			var expected_loc_str = $"{script_loc}#{line}#{column}";
			Assert.Equal (expected_loc_str, loc_str);
		}

		[Fact]
		public async Task CheckThatAllSourcesAreSent () {
			var insp = new Inspector ();
			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready();
			//all sources are sent before runtime ready is sent, nothing to check
			await insp.Ready ();
			Assert.Contains ("dotnet://debugger-test.dll/debugger-test.cs", scripts.Values);
			Assert.Contains ("dotnet://debugger-test.dll/debugger-test2.cs", scripts.Values);
			Assert.Contains ("dotnet://Simple.Dependency.dll/dependency.cs", scripts.Values);
		}

		[Fact]
		public async Task CreateGoodBreakpoint () {
			var insp = new Inspector ();

			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready ();
			await insp.Ready (async (cli, token) => {
				ctx = new DebugTestContext (cli, insp, token, scripts);

				var bp1_res = await SetBreakpoint ("dotnet://debugger-test.dll/debugger-test.cs", 5, 2, ctx);

				Assert.EndsWith ("debugger-test.cs", bp1_res.Value ["breakpointId"].ToString());
				Assert.Equal (1, bp1_res.Value ["locations"]?.Value<JArray> ()?.Count);
			
				var loc = bp1_res.Value ["locations"]?.Value<JArray> ()[0];

				Assert.NotNull (loc ["scriptId"]);
				Assert.Equal("dotnet://debugger-test.dll/debugger-test.cs", scripts [loc["scriptId"]?.Value<string> ()]);
				Assert.Equal (5, loc ["lineNumber"]);
				Assert.Equal (2, loc ["columnNumber"]);
			});
		}

		[Fact]
		public async Task CreateBadBreakpoint () {
			var insp = new Inspector ();

			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready ();
			await insp.Ready (async (cli, token) => {
				var bp1_req = JObject.FromObject(new {
					lineNumber = 5,
					columnNumber = 2,
					url = "dotnet://debugger-test.dll/this-file-doesnt-exist.cs",
				});

				var bp1_res = await cli.SendCommand ("Debugger.setBreakpointByUrl", bp1_req, token);

				Assert.True (bp1_res.IsOk);
				Assert.Empty (bp1_res.Value["locations"].Values<object>());
				//Assert.Equal ((int)MonoErrorCodes.BpNotFound, bp1_res.Error ["code"]?.Value<int> ());
			});
		}

		[Fact]
		public async Task CreateGoodBreakpointAndHit () {
			var insp = new Inspector ();

			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready ();
			await insp.Ready (async (cli, token) => {
				ctx = new DebugTestContext (cli, insp, token, scripts);

				var bp = await SetBreakpoint ("dotnet://debugger-test.dll/debugger-test.cs", 5, 2, ctx);

				var eval_req = JObject.FromObject(new {
					expression = "window.setTimeout(function() { invoke_add(); }, 1);",
				});

				await EvaluateAndCheck (
					"window.setTimeout(function() { invoke_add(); }, 1);",
					"dotnet://debugger-test.dll/debugger-test.cs", 5, 2,
					"IntAdd", ctx,
					wait_for_event_fn: (pause_location) => {
						Assert.Equal ("other", pause_location ["reason"]?.Value<string> ());
						Assert.Equal (bp.Value["breakpointId"]?.ToString(), pause_location ["hitBreakpoints"]?[0]?.Value<string> ());

						var top_frame = pause_location ["callFrames"][0];
						Assert.Equal ("IntAdd", top_frame ["functionName"].Value<string>());
						Assert.Contains ("debugger-test.cs", top_frame ["url"].Value<string> ());

						CheckLocation ("dotnet://debugger-test.dll/debugger-test.cs", 3, 41, scripts, top_frame["functionLocation"]);

						//now check the scope
						var scope = top_frame ["scopeChain"][0];
						Assert.Equal ("local", scope ["type"]);
						Assert.Equal ("IntAdd", scope ["name"]);

						Assert.Equal ("object", scope ["object"]["type"]);
						Assert.Equal ("dotnet:scope:0", scope ["object"]["objectId"]);
						CheckLocation ("dotnet://debugger-test.dll/debugger-test.cs", 3, 41, scripts, scope["startLocation"]);
						CheckLocation ("dotnet://debugger-test.dll/debugger-test.cs", 9, 1, scripts, scope["endLocation"]);
						return Task.CompletedTask;
					}
				);

			});
		}

		[Fact]
		public async Task ExceptionThrownInJS () {
			var insp = new Inspector ();

			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready ();
			await insp.Ready (async (cli, token) => {
				var eval_req = JObject.FromObject(new {
					expression = "invoke_bad_js_test();"
				});

				var eval_res = await cli.SendCommand ("Runtime.evaluate", eval_req, token);
				Assert.True (eval_res.IsErr);
				Assert.Equal ("Uncaught", eval_res.Error ["exceptionDetails"]? ["text"]? .Value<string> ());
			});
		}

		[Fact]
		public async Task ExceptionThrownInJSOutOfBand () {
			var insp = new Inspector ();

			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready ();
			await insp.Ready (async (cli, token) => {
				ctx = new DebugTestContext (cli, insp, token, scripts);

				await SetBreakpoint ("/debugger-driver.html", 27, 2, ctx);

				var eval_req = JObject.FromObject(new {
					expression = "window.setTimeout(function() { invoke_bad_js_test(); }, 1);",
				});

				var eval_res = await cli.SendCommand ("Runtime.evaluate", eval_req, token);
				// Response here will be the id for the timer from JS!
				Assert.True (eval_res.IsOk);

				var ex = await Assert.ThrowsAsync<ArgumentException> (async () => await insp.WaitFor("Runtime.exceptionThrown"));
				var ex_json = JObject.Parse (ex.Message);
				Assert.Equal (dicFileToUrl["/debugger-driver.html"], ex_json ["exceptionDetails"]? ["url"]? .Value<string> ());
			});

		}

		void CheckNumber (JToken locals, string name, int value) {
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;
				var val = l["value"];
				Assert.Equal ("number", val ["type"]?.Value<string> ());
				Assert.Equal (value, val["value"]?.Value <int> ());
				return;
			}
			Assert.True(false, $"Could not find variable '{name}'");
		}

		void CheckString (JToken locals, string name, string value) {
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;
				var val = l["value"];
				if (value == null) {
						Assert.Equal ("object", val ["type"]?.Value<string> ());
						Assert.Equal ("null", val["subtype"]?.Value<string> ());
				} else {
						Assert.Equal ("string", val ["type"]?.Value<string> ());
						Assert.Equal (value, val["value"]?.Value <string> ());
				}
				return;
			}
			Assert.True(false, $"Could not find variable '{name}'");
		}

		JToken CheckObject (JToken locals, string name, string class_name, string subtype=null, bool is_null=false) {
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;

				var val = l["value"];
				Assert.Equal ("object", val ["type"]?.Value<string> ());
				Assert.True (val ["isValueType"] == null || !val ["isValueType"].Value<bool> ());
				Assert.Equal (class_name, val ["className"]?.Value<string> ());

				var has_null_subtype = val ["subtype"] != null && val ["subtype"]?.Value<string> () == "null";
				Assert.Equal (is_null, has_null_subtype);
				if (subtype != null)
					Assert.Equal (subtype, val ["subtype"]?.Value<string> ());

				return l;
			}
			Assert.True(false, $"Could not find variable '{name}'");
			return null;
		}

		async Task CheckDateTime (JToken locals, string name, DateTime expected, DebugTestContext ctx)
			=> await CheckObjectOnLocals (locals, name, ctx,
				test_fn: (members) => {
					//members.HasNumber ("_dateData", 6.3713531106E+17);
					//members.HasNumber ("InternalTicks", 6.3713531106E+17);

					// not checking everything
					members.HasNumber ("Year", expected.Year);
					members.HasNumber ("Month", expected.Month);
					members.HasNumber ("Day", expected.Day);
					members.HasNumber ("Hour", expected.Hour);
					members.HasNumber ("Minute", expected.Minute);
					members.HasNumber ("Second", expected.Second);

					// FIXME: check some float properties too

					CheckEnum (members, "DayOfWeek", "System.DayOfWeek", expected.DayOfWeek.ToString ());

					//members.HasNumber ("Kind", 6);
					//FIXME: `Date` property
				}
			);

		JToken CheckBool (JToken locals, string name, bool expected)
		{
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;

				var val = l["value"];
				Assert.Equal ("boolean", val ["type"]?.Value<string> ());
				if (val ["value"] == null)
					Assert.True (false, "expected bool value not found for variable named {name}");
				Assert.Equal (expected, val ["value"]?.Value<bool> ());

				return l;
			}
			Assert.True(false, $"Could not find variable '{name}'");
			return null;
		}

		void CheckValueType (JToken locals, string name, string class_name) {
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;

				var val = l["value"];
				Assert.Equal ("object", val ["type"]?.Value<string> ());
				Console.WriteLine ($"-- isValueType: '{val ["isValueType"]?.Value<bool> ()}'");
				Assert.True (val ["isValueType"] != null && val ["isValueType"].Value<bool> ());
				Assert.Equal (class_name, val ["className"]?.Value<string> ());
				return;
			}
			Assert.True(false, $"Could not find variable '{name}'");
		}

		void CheckArray (JToken locals, string name, string class_name) {
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;

				var val = l["value"];
				Assert.Equal ("object", val ["type"]?.Value<string> ());
				Assert.Equal ("array", val ["subtype"]?.Value<string> ());
				Assert.Equal (class_name, val ["className"]?.Value<string> ());

				//FIXME: elements?
				return;
			}
			Assert.True(false, $"Could not find variable '{name}'");
		}

		void CheckFunction (JToken locals, string name, string description, string subtype=null) {
			Console.WriteLine ($"** Locals: {locals.ToString ()}");
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;

				var val = l["value"];
				Assert.Equal ("function", val ["type"]?.Value<string> ());
				Assert.Equal (description, val ["description"]?.Value<string> ());
				Assert.Equal (subtype, val ["subtype"]?.Value<string> ());
				return;
			}
			Assert.True(false, $"Could not find variable '{name}'");
		}

		void CheckEnum (JToken locals, string name, string class_name, string descr)
		{
			var local = locals?.Where (l => l ["name"].Value<string> () == name).FirstOrDefault ();
			if (local == null)
				Assert.True(false, $"Could not find variable '{name}'");

			var val = local ["value"];
			Assert.Equal ("object", val ["type"]?.Value<string> ());
			Assert.True (val ["isEnum"] != null && val ["isEnum"].Value<bool> ());
			Assert.Equal (class_name, val ["className"]?.Value<string> ());
			Assert.Equal (descr, val ["description"]?.Value<string> ());
		}

		[Fact]
		public async Task InspectLocalsAtBreakpointSite () =>
			await CheckInspectLocalsAtBreakpointSite (
				"dotnet://debugger-test.dll/debugger-test.cs", 5, 2, "x",
				"window.setTimeout(function() { invoke_add(); }, 1);",
				test_fn: (locals) => {
					CheckNumber (locals, "a", 10);
					CheckNumber (locals, "b", 20);
					CheckNumber (locals, "c", 30);
					CheckNumber (locals, "d", 0);
					CheckNumber (locals, "e", 0);
				}
			);

		[Fact]
		public async Task InspectLocalsWithDelegatesAtBreakpointSite () =>
			await CheckInspectLocalsAtBreakpointSite (
				"dotnet://debugger-test.dll/debugger-test.cs", 41, 2, "x",
				"window.setTimeout(function() { invoke_delegates_test (); }, 1);",
				test_fn: (locals) => {
					CheckObject (locals, "fn_func", "System.Func<Math, bool>");
					CheckObject (locals, "fn_func_null", "System.Func<Math, bool>", is_null: true);
					CheckArray (locals, "fn_func_arr", "System.Func<Math, bool>[]");
					CheckFunction (locals, "fn_del", "Math.IsMathNull");
					CheckObject (locals, "fn_del_null", "Math.IsMathNull", is_null: true);
					CheckArray (locals, "fn_del_arr", "Math.IsMathNull[]");

					// Unused locals
					CheckObject (locals, "fn_func_unused", "System.Func<Math, bool>", is_null: true);
					CheckObject (locals, "fn_func_null_unused", "System.Func<Math, bool>", is_null: true);
					CheckObject (locals, "fn_func_arr_unused", "System.Func<Math, bool>[]", is_null: true);

					CheckObject (locals, "fn_del_unused", "Math.IsMathNull", is_null: true);
					CheckObject (locals, "fn_del_null_unused", "Math.IsMathNull", is_null: true);
					CheckObject (locals, "fn_del_arr_unused", "Math.IsMathNull[]", is_null: true);
				}
			);

		[Fact]
		public async Task InspectLocalsWithGenericTypesAtBreakpointSite () =>
			await CheckInspectLocalsAtBreakpointSite (
				"dotnet://debugger-test.dll/debugger-test.cs", 62, 2, "x",
				"window.setTimeout(function() { invoke_generic_types_test (); }, 1);",
				test_fn: (locals) => {
					CheckObject (locals, "list", "System.Collections.Generic.Dictionary<Math[], Math.IsMathNull>");
					CheckObject (locals, "list_null", "System.Collections.Generic.Dictionary<Math[], Math.IsMathNull>", is_null: true);

					CheckArray (locals, "list_arr", "System.Collections.Generic.Dictionary<Math[], Math.IsMathNull>[]");
					CheckObject (locals, "list_arr_null", "System.Collections.Generic.Dictionary<Math[], Math.IsMathNull>[]", is_null: true);

					// Unused locals
					CheckObject (locals, "list_unused", "System.Collections.Generic.Dictionary<Math[], Math.IsMathNull>", is_null: true);
					CheckObject (locals, "list_null_unused", "System.Collections.Generic.Dictionary<Math[], Math.IsMathNull>", is_null: true);

					CheckObject (locals, "list_arr_unused", "System.Collections.Generic.Dictionary<Math[], Math.IsMathNull>[]", is_null: true);
					CheckObject (locals, "list_arr_null_unused", "System.Collections.Generic.Dictionary<Math[], Math.IsMathNull>[]", is_null: true);
				}
			);

		async Task CheckInspectLocalsAtBreakpointSite (string url_key, int line, int column, string function_name, string eval_expression, Action<JToken> test_fn) {
			var insp = new Inspector ();
			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready ();
			await insp.Ready (async (cli, token) => {
				ctx = new DebugTestContext (cli, insp, token, scripts);

				var bp = await SetBreakpoint (url_key, line, column, ctx);

				await EvaluateAndCheck (
					eval_expression, url_key, line, column,
					function_name, ctx,
					wait_for_event_fn: (pause_location) => {
						//make sure we're on the right bp

						Assert.Equal (bp.Value ["breakpointId"]?.ToString (), pause_location ["hitBreakpoints"]?[0]?.Value<string> ());

						var top_frame = pause_location ["callFrames"][0];

						var scope = top_frame ["scopeChain"][0];
						Assert.Equal ("dotnet:scope:0", scope ["object"]["objectId"]);
						return Task.CompletedTask;
					},
					locals_fn: (locals) => {
						if (test_fn != null)
							test_fn (locals);
					}
				);
			});
		}

		[Fact]
		public async Task RuntimeGetPropertiesWithInvalidScopeIdTest () {
			var insp = new Inspector ();
			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready ();
			await insp.Ready (async (cli, token) => {
				ctx = new DebugTestContext (cli, insp, token, scripts);

				var bp = await SetBreakpoint ("dotnet://debugger-test.dll/debugger-test.cs", 41, 2, ctx);

				await EvaluateAndCheck (
					"window.setTimeout(function() { invoke_delegates_test (); }, 1);",
					"dotnet://debugger-test.dll/debugger-test.cs", 41, 2,
					"IntAdd", ctx,
					wait_for_event_fn: async (pause_location) => {
						//make sure we're on the right bp
						Assert.Equal (bp.Value ["breakpointId"]?.ToString (), pause_location ["hitBreakpoints"]?[0]?.Value<string> ());

						var top_frame = pause_location ["callFrames"][0];

						var scope = top_frame ["scopeChain"][0];
						Assert.Equal ("dotnet:scope:0", scope ["object"]["objectId"]);

						// Try to get an invalid scope!
						var get_prop_req = JObject.FromObject(new {
							objectId = "dotnet:scope:23490871",
						});

						// FIXME: um this is likely failing .. or timign out? how does this work at all?
						var frame_props = await cli.SendCommand ("Runtime.getProperties", get_prop_req, token);
						Assert.True (frame_props.IsErr);
					}
				);
			});
		}

		[Fact]
		public async Task TrivalStepping () {
			var insp = new Inspector ();
			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready ();
			await insp.Ready (async (cli, token) => {
				ctx = new DebugTestContext (cli, insp, token, scripts);

				var bp = await SetBreakpoint ("dotnet://debugger-test.dll/debugger-test.cs", 5, 2, ctx);

				await EvaluateAndCheck (
					"window.setTimeout(function() { invoke_add(); }, 1);",
					"dotnet://debugger-test.dll/debugger-test.cs", 5, 2,
					"IntAdd", ctx,
					wait_for_event_fn: (pause_location) => {
						//make sure we're on the right bp
						Assert.Equal (bp.Value ["breakpointId"]?.ToString (), pause_location ["hitBreakpoints"]?[0]?.Value<string> ());

						var top_frame = pause_location ["callFrames"][0];
						CheckLocation ("dotnet://debugger-test.dll/debugger-test.cs", 3, 41, scripts, top_frame["functionLocation"]);
						return Task.CompletedTask;
					}
				);

				await StepAndCheck (StepKind.Over, "dotnet://debugger-test.dll/debugger-test.cs", 6, 2, "IntAdd", ctx,
						wait_for_event_fn: (pause_location) => {
							var top_frame = pause_location ["callFrames"][0];
							CheckLocation ("dotnet://debugger-test.dll/debugger-test.cs", 3, 41, scripts, top_frame["functionLocation"]);
							return Task.CompletedTask;
						}
				);
			});
		}

		[Fact]
		public async Task InspectLocalsDuringStepping () {
			var insp = new Inspector ();
			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready();
			await insp.Ready (async (cli, token) => {
				ctx = new DebugTestContext (cli, insp, token, scripts);

				var debugger_test_loc = "dotnet://debugger-test.dll/debugger-test.cs";
				await SetBreakpoint (debugger_test_loc, 4, 2, ctx);

				await EvaluateAndCheck (
					"window.setTimeout(function() { invoke_add(); }, 1);",
					debugger_test_loc, 4, 2, "IntAdd", ctx,
					locals_fn: (locals) => {
						CheckNumber (locals, "a", 10);
						CheckNumber (locals, "b", 20);
						CheckNumber (locals, "c", 0);
						CheckNumber (locals, "d", 0);
						CheckNumber (locals, "e", 0);
					}
				);

				await StepAndCheck (StepKind.Over, debugger_test_loc, 5, 2, "IntAdd", ctx,
					locals_fn: (locals) => {
						CheckNumber (locals, "a", 10);
						CheckNumber (locals, "b", 20);
						CheckNumber (locals, "c", 30);
						CheckNumber (locals, "d", 0);
						CheckNumber (locals, "e", 0);
					}
				);

				//step and get locals
				await StepAndCheck (StepKind.Over, debugger_test_loc, 6, 2, "IntAdd", ctx,
					locals_fn: (locals) => {
						CheckNumber (locals, "a", 10);
						CheckNumber (locals, "b", 20);
						CheckNumber (locals, "c", 30);
						CheckNumber (locals, "d", 50);
						CheckNumber (locals, "e", 0);
					}
				);
			});
		}

		[Fact]
		public async Task InspectLocalsInPreviousFramesDuringSteppingIn2 () {
			var insp = new Inspector ();
			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready();
			await insp.Ready (async (cli, token) => {
				ctx = new DebugTestContext (cli, insp, token, scripts);

				var dep_cs_loc = "dotnet://Simple.Dependency.dll/dependency.cs";
				await SetBreakpoint (dep_cs_loc, 24, 2, ctx);

				var debugger_test_loc = "dotnet://debugger-test.dll/debugger-test.cs";

				// Will stop in Complex.DoEvenMoreStuff
				var pause_location = await EvaluateAndCheck (
					"window.setTimeout(function() { invoke_use_complex (); }, 1);",
					dep_cs_loc, 24, 2, "DoEvenMoreStuff", ctx,
					locals_fn: (locals) => {
						Assert.Single (locals);
						CheckObject (locals, "this", "Simple.Complex");
					}
				);

				await CheckObjectOnFrameLocals (pause_location["callFrames"][0], "this", ctx,
					test_fn: (props) => {
						Assert.Equal (3, props.Count());
						CheckNumber (props, "A", 10);
						CheckString (props, "B", "xx");
						CheckObject (props, "c", "object");
					}
				);

				// Check UseComplex frame
				await CheckLocalsOnFrame (pause_location ["callFrames"][3], debugger_test_loc, 17, 2, "UseComplex", ctx,
					test_fn: (locals_m1) => {
						Assert.Equal (7, locals_m1.Count());

						CheckNumber (locals_m1, "a", 10);
						CheckNumber (locals_m1, "b", 20);
						CheckObject (locals_m1, "complex", "Simple.Complex");
						CheckNumber (locals_m1, "c", 30);
						CheckNumber (locals_m1, "d", 50);
						CheckNumber (locals_m1, "e", 60);
						CheckNumber (locals_m1, "f", 0);
					}
				);

				await CheckObjectOnFrameLocals (pause_location["callFrames"][3], "complex", ctx,
					test_fn: (props) => {
						Assert.Equal (3, props.Count());
						CheckNumber (props, "A", 10);
						CheckString (props, "B", "xx");
						CheckObject (props, "c", "object");
					}
				);

				pause_location = await StepAndCheck (StepKind.Over, dep_cs_loc, 16, 2, "DoStuff", ctx, times: 2);
				// Check UseComplex frame again
				await CheckLocalsOnFrame (pause_location ["callFrames"][1], debugger_test_loc, 17, 2, "UseComplex", ctx,
					test_fn: (locals_m1) => {
						Assert.Equal (7, locals_m1.Count());

						CheckNumber (locals_m1, "a", 10);
						CheckNumber (locals_m1, "b", 20);
						CheckObject (locals_m1, "complex", "Simple.Complex");
						CheckNumber (locals_m1, "c", 30);
						CheckNumber (locals_m1, "d", 50);
						CheckNumber (locals_m1, "e", 60);
						CheckNumber (locals_m1, "f", 0);
					}
				);

				await CheckObjectOnFrameLocals (pause_location["callFrames"][1], "complex", ctx,
					test_fn: (props) => {
						Assert.Equal (3, props.Count());
						CheckNumber (props, "A", 10);
						CheckString (props, "B", "xx");
						CheckObject (props, "c", "object");
					}
				);
			});
		}

		[Fact]
		public async Task InspectLocalsInPreviousFramesDuringSteppingIn () {
			var insp = new Inspector ();
			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready();
			await insp.Ready (async (cli, token) => {
				ctx = new DebugTestContext (cli, insp, token, scripts);

				var debugger_test_loc = "dotnet://debugger-test.dll/debugger-test.cs";
				await SetBreakpoint (debugger_test_loc, 100, 3, ctx);

				// Will stop in InnerMethod
				var wait_res = await EvaluateAndCheck (
					"window.setTimeout(function() { invoke_outer_method(); }, 1);",
					debugger_test_loc, 100, 3, "InnerMethod", ctx,
					locals_fn: (locals) => {
						Assert.Equal (4, locals.Count());
						CheckNumber (locals, "i", 5);
						CheckNumber (locals, "j", 24);
						CheckString (locals, "foo_str", "foo");
						//FIXME: check members for this
						CheckObject (locals, "this", "Math.NestedInMath");
					}
				);

				var this_props = await CheckObjectOnFrameLocals (wait_res["callFrames"][0], "this", ctx,
					test_fn: (props) => {
						Assert.Equal (2, props.Count());
						CheckObject (props, "m", "Math");
						CheckValueType (props, "SimpleStructProperty", "Math.SimpleStruct");
					}
				);

				//FIXME: um previous frames stuff!
				var ss_props = await CheckObjectOnLocals (this_props, "SimpleStructProperty", ctx,
					test_fn: (props) => {
						Console.WriteLine ($"--- SimpleStructProperty's props: {props.ToString ()}");
						Assert.Equal (6, props.Count());
						CheckNumber (props, "num", 0xDDEEFFA);
						CheckString (props, "str_member", "SimpleStruct..ctor got str: set in InnerMethod");
						CheckValueType (props, "dt", "System.DateTime");
						//FIXME: check fields
						CheckValueType (props, "gs", "Math.GenericStruct<System.DateTime>");
						CheckObject (props, "m", "Math");
						//FIXME: check fields
						CheckValueType (props, "another_struct", "Math.AnotherStruct");
					}
				);

				await CheckDateTime (ss_props, "dt", new DateTime (2020, 1, 2, 3, 5, 6), ctx);

#if false
				// Check OuterMethod frame
				await CheckLocalsOnFrame (wait_res ["callFrames"][1], debugger_test_loc, 76, 2, "OuterMethod", ctx,
					test_fn: (locals_m1) => {
						Assert.Equal (5, locals_m1.Count());
						// FIXME: Failing test CheckNumber (locals_m1, "i", 5);
						// FIXME: Failing test CheckString (locals_m1, "text", "Hello");
						CheckNumber (locals_m1, "new_i", 0);
						CheckNumber (locals_m1, "k", 0);
						CheckObject (locals_m1, "nim", "Math.NestedInMath");
					}
				);

				// step back into OuterMethod
				await StepAndCheck (StepKind.Over, "dotnet://debugger-test.dll/debugger-test.cs", 80, 2, "OuterMethod", ctx, times: 7,
					locals_fn: (locals) => {
						Assert.Equal (5, locals.Count());

						// FIXME: Failing test CheckNumber (locals_m1, "i", 5);
						CheckString (locals, "text", "Hello");
						// FIXME: Failing test CheckNumber (locals, "new_i", 24);
						CheckNumber (locals, "k", 19);
						CheckObject (locals, "nim", "Math.NestedInMath");
					}
				);

				//await StepAndCheck (StepKind.Over, "dotnet://debugger-test.dll/debugger-test.cs", 81, 2, "OuterMethod", ctx, times: 2);

				// step into InnerMethod2
				await StepAndCheck (StepKind.Into, "dotnet://debugger-test.dll/debugger-test.cs", 85, 1, "InnerMethod2", ctx,
					locals_fn: (locals) => {
						// umm use JOBject instead of JToken?
						Assert.Equal (3, locals.Count());

						CheckString (locals, "s", "test string");
						//out var: CheckNumber (locals, "k", 0);
						CheckNumber (locals, "i", 24);
					}
				);

				await StepAndCheck (StepKind.Over, "dotnet://debugger-test.dll/debugger-test.cs", 89, 1, "InnerMethod2", ctx, times: 4,
					locals_fn: (locals) => {
						// umm use JOBject instead of JToken?
						Assert.Equal (3, locals.Count());

						CheckString (locals, "s", "test string");
						// FIXME: Failing test CheckNumber (locals, "k", 34);
						CheckNumber (locals, "i", 24);
					}
				);

				await StepAndCheck (StepKind.Over, "dotnet://debugger-test.dll/debugger-test.cs", 81, 2, "OuterMethod", ctx, times: 2,
					locals_fn: (locals) => {
						Assert.Equal (5, locals.Count());

						CheckString (locals, "text", "Hello");
						// FIXME: failing test CheckNumber (locals, "i", 5);
						CheckNumber (locals, "new_i", 22);
						CheckNumber (locals, "k", 34);
						CheckObject (locals, "nim", "Math.NestedInMath");
					}
				);
#endif
			});
		}

		[Fact]
		public async Task InspectLocalsDuringSteppingIn () {
			var insp = new Inspector ();
			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready();
			await insp.Ready (async (cli, token) => {
				ctx = new DebugTestContext (cli, insp, token, scripts);

				await SetBreakpoint ("dotnet://debugger-test.dll/debugger-test.cs", 75, 2, ctx);

				await EvaluateAndCheck ("window.setTimeout(function() { invoke_outer_method(); }, 1);",
					"dotnet://debugger-test.dll/debugger-test.cs", 75, 2, "OuterMethod", ctx,
					locals_fn: (locals) => {
						Assert.Equal (5, locals.Count());

						CheckObject (locals, "nim", "Math.NestedInMath");
						CheckNumber (locals, "i", 0);
						CheckNumber (locals, "k", 0);
						CheckNumber (locals, "new_i", 0);
						CheckString (locals, "text", null);
					}
				);

				await StepAndCheck (StepKind.Over, "dotnet://debugger-test.dll/debugger-test.cs", 76, 2, "OuterMethod", ctx,
					locals_fn: (locals) => {
						Assert.Equal (5, locals.Count());

						CheckObject (locals, "nim", "Math.NestedInMath");
						// FIXME: Failing test CheckNumber (locals, "i", 5);
						CheckNumber (locals, "k", 0);
						CheckNumber (locals, "new_i", 0);
						CheckString (locals, "text", "Hello");
					}
				);

				// Step into InnerMethod
				await StepAndCheck (StepKind.Into, "dotnet://debugger-test.dll/debugger-test.cs", 94, 2, "InnerMethod", ctx);
				Console.WriteLine ($"--------------- calling stepAndCheck ---------------");
				await StepAndCheck (StepKind.Over, "dotnet://debugger-test.dll/debugger-test.cs", 98, 3, "InnerMethod", ctx, times: 5,
					locals_fn: (locals) => {
						Assert.Equal (4, locals.Count());

						CheckNumber (locals, "i", 5);
						CheckNumber (locals, "j", 15);
						CheckString (locals, "foo_str", "foo");
						CheckObject (locals, "this", "Math.NestedInMath");
					}
				);

				Console.WriteLine ($"--------------- calling stepAndCheck FOR stepping to outerMethod---------------");
				// Step back to OuterMethod
				await StepAndCheck (StepKind.Over, "dotnet://debugger-test.dll/debugger-test.cs", 77, 2, "OuterMethod", ctx, times: 6,
					locals_fn: (locals) => {
						Assert.Equal (5, locals.Count());

						CheckObject (locals, "nim", "Math.NestedInMath");
						// FIXME: Failing test CheckNumber (locals, "i", 5);
						CheckNumber (locals, "k", 0);
						CheckNumber (locals, "new_i", 24);
						CheckString (locals, "text", "Hello");
					}
				);
			});
		}

		[Fact]
		public async Task InspectLocalsInAsyncMethods () {
			var insp = new Inspector ();
			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready();
			await insp.Ready (async (cli, token) => {
				ctx = new DebugTestContext (cli, insp, token, scripts);
				var debugger_test_loc = "dotnet://debugger-test.dll/debugger-test.cs";

				await SetBreakpoint (debugger_test_loc, 108, 3, ctx);
				await SetBreakpoint (debugger_test_loc, 123, 3, ctx);

				// Will stop in Asyncmethod0
				var wait_res = await EvaluateAndCheck (
					"window.setTimeout(function() { invoke_async_method_with_await(); }, 1);",
					debugger_test_loc, 108, 3, "MoveNext", ctx, //FIXME:
					locals_fn: (locals) => {
						Assert.Equal (4, locals.Count());
						CheckString (locals, "s", "string from js");
						CheckNumber (locals, "i", 42);
						CheckString (locals, "local0", "value0");
						CheckObject (locals, "this", "Math.NestedInMath");
					}
				);

				// TODO: previous frames have async machinery details, so no point checking that right now

				var pause_loc = await SendCommandAndCheck (null, "Debugger.resume", debugger_test_loc, 123, 3, "AsyncMethodNoReturn", ctx,
					locals_fn: (locals) => {
						Assert.Equal (4, locals.Count());
						CheckString (locals, "str", "AsyncMethodNoReturn's local");
						CheckObject (locals, "this", "Math.NestedInMath");
						//FIXME: check fields
						CheckValueType (locals, "ss", "Math.SimpleStruct");
						CheckArray (locals, "ss_arr", "Math.SimpleStruct[]");
						// TODO: struct fields
					}
				);

				await CheckObjectOnFrameLocals (pause_loc ["callFrames"][0], "this", ctx,
					test_fn: (props) => {
						Assert.Equal (2, props.Count ());
						CheckObject (props, "m", "Math");
						CheckValueType (props, "SimpleStructProperty", "Math.SimpleStruct");
					}
				);

				// TODO: Check `this` properties
			});
		}

		[Fact]
		public async Task InspectLocalsWithStructs () {
			var insp = new Inspector ();
			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready();
			await insp.Ready (async (cli, token) => {
				ctx = new DebugTestContext (cli, insp, token, scripts);
				var debugger_test_loc = "dotnet://debugger-test.dll/debugger-test.cs";

				await SetBreakpoint (debugger_test_loc, 139, 3, ctx);

				var pause_location = await EvaluateAndCheck (
					"window.setTimeout(function() { invoke_method_with_structs(); }, 1);",
					debugger_test_loc, 139, 3, "MethodWithStructs", ctx,
					locals_fn: (locals) => {
						Assert.Equal (4, locals.Count ());
						CheckObject (locals, "m_local", "Math");
						CheckValueType (locals, "ss_local", "Math.SimpleStruct");
						CheckValueType (locals, "gs", "Math.GenericStruct<Math>");
						CheckArray (locals, "ss_arr", "Math.SimpleStruct[]");
					}
				);

				// Check m_local's properties
				var m_local_props = await CheckObjectOnFrameLocals (pause_location ["callFrames"][0], "m_local", ctx,
					test_fn: (props) => {
						Console.WriteLine ($"--- m_local's props: {props.ToString ()}");
						Assert.Equal (2, props.Count());

						CheckString (props, "StringField", "value set in MethodWithStructs");
						CheckValueType (props, "StructFieldInMathClass", "Math.AnotherStruct");
					}
				);

				Console.WriteLine ($"--------------------------------------- Fetching props for StructFieldInMathClass --------------");
				// Check m_local.StructFieldInMathClass's properties
				await CheckObjectOnLocals (m_local_props, "StructFieldInMathClass", ctx,
					test_fn: (props) => {
						Console.WriteLine ($"--- StructFieldInMathClass's props: {props.ToString ()}");
						Assert.Equal (4, props.Count());
						
						//props.HasString ("Name", "Set on math.StructFieldInMathClass in MethodWithStructs");
						//CheckBool (props, "BoolField", false);
						//CheckEnum (props, "RGB", "Math.RGB", "Blue");
						//CheckEnum (props, "Options", "Math.Options", "Option3");

					}
				);

				// Check SimpleStruct
				var ss_props = await CheckObjectOnFrameLocals (pause_location ["callFrames"][0], "ss_local", ctx,
					test_fn: (props) => {
						Console.WriteLine ($"--- SimpleStructProperty's props: {props.ToString ()}");
						Assert.Equal (6, props.Count());
						props.HasNumber ("num", 0xDDEEFFA);
						props.HasString ("str_member", "SimpleStruct..ctor got str: set in MethodWithStructs");
						props.HasObject ("dt", "System.DateTime");

						//FIXME: check fields
						props.HasObject ("gs", "Math.GenericStruct<System.DateTime>");
						props.HasObject ("m", "Math");
						props.HasObject ("another_struct", "Math.AnotherStruct");
					}
				);

				// Check SimpleStruct members
				await CheckObjectOnLocals (ss_props, "another_struct", ctx,
					test_fn: (props) => {
						Assert.Equal (4, props.Count());
						props.HasString ("Name", "Name for AnotherStruct set in SimpleStruct..ctor");
						CheckBool (props, "BoolField", false);
						CheckEnum (props, "RGB", "Math.RGB", "Green");
						CheckEnum (props, "Options", "Math.Options", "Option2");
					}
				);

				await CheckDateTime (ss_props, "dt", new DateTime (2020, 1, 2, 3, 5, 6), ctx);
				
				await CheckObjectOnLocals (ss_props, "gs", ctx,
					test_fn: (props) => {
						Assert.Equal (2, props.Count());
						props.HasString ("StringField", "StringField member of a GenericStruct");
						CheckObject (props, "List", "System.Collections.Generic.List<System.DateTime>");
					}
				);
			});
		}

		async Task<JObject> StepAndCheck (StepKind kind, string script_loc, int line, int column, string function_name, DebugTestContext ctx,
							Func<JObject, Task> wait_for_event_fn = null, Action<JToken> locals_fn = null, int times=1)
		{
			for (int i = 0; i < times - 1; i ++) {
				var res = await SendCommandAndCheck (null, $"Debugger.step{kind.ToString ()}", null, -1, -1, null, ctx);
				//Console.WriteLine ($"Step #{i}, res: {res["callFrames"]?[0]?["location"]}");
				Console.WriteLine ($"Step #{i}, res: {res["callFrames"]?[0]}");
			}

			// Check for method/line etc only at the last step
			return await SendCommandAndCheck (
						null, $"Debugger.step{kind.ToString ()}", script_loc, line, column, function_name, ctx,
						wait_for_event_fn: wait_for_event_fn,
						locals_fn: locals_fn);
		}

		async Task<JObject> EvaluateAndCheck (string expression, string script_loc, int line, int column, string function_name, DebugTestContext ctx,
								Func<JObject, Task> wait_for_event_fn = null, Action<JToken> locals_fn = null)
			=> await SendCommandAndCheck (
						JObject.FromObject (new { expression = expression }),
						"Runtime.evaluate", script_loc, line, column, function_name, ctx,
						wait_for_event_fn: wait_for_event_fn,
						locals_fn: locals_fn);

		async Task<JObject> SendCommandAndCheck (JObject args, string method, string script_loc, int line, int column, string function_name, DebugTestContext ctx,
								Func<JObject, Task> wait_for_event_fn = null, Action<JToken> locals_fn = null, string waitForEvent = Inspector.PAUSE)
		{
			var res = await ctx.cli.SendCommand (method, args, ctx.token);
			if (!res.IsOk) {
				Console.WriteLine ($"Failed to run command {method} with args: {args?.ToString ()}\nresult: {res.Error.ToString ()}");
				Assert.True (false, $"SendCommand for {method} failed with {res.Error.ToString ()}");
			}

			var wait_res = await ctx.insp.WaitFor(waitForEvent);

			if (script_loc != null)
				CheckLocation (script_loc, line, column, ctx.scripts, wait_res ["callFrames"][0]["location"]);

			if (wait_for_event_fn != null)
				await wait_for_event_fn (wait_res);

			if (locals_fn != null)
				await CheckLocalsOnFrame (wait_res ["callFrames"][0], ctx, locals_fn);

			return wait_res;
		}

		void CheckLocals (JArray expected, JToken actual)
		{
			int num = expected.Count ();
			Assert.Equal (num, actual.Count());
			for (int i = 0; i < num; i ++) {
				var ejt = expected [i];
				if (ejt ["skip"]?.Value<bool> () == true)
					continue;

				switch (ejt ["type"].Value<string> ()) {
				case "object":
					CheckObject (actual, ejt ["name"]?.Value<string> (), ejt ["typeName"]?.Value<string> (), subtype: ejt ["subtype"]?.Value<string> ());
					break;
				case "array": 
					CheckArray (actual, ejt ["name"]?.Value<string> (), ejt ["typeName"]?.Value<string> ());
					break;
				case "number":
					CheckNumber (actual, ejt ["name"]?.Value<string> (), ejt ["value"].Value<int> ());
					break;
				case "string":
					CheckString (actual, ejt ["name"]?.Value<string> (), ejt ["value"].Value<string> ());
					break;
				default:
					throw new NotImplementedException (ejt ["type"].Value<string> ());
				}

			}
		}

		async Task CheckLocalsOnFrame (JToken frame, string script_loc, int line, int column, string function_name, DebugTestContext ctx, Action<JToken> test_fn = null)
		{
			CheckLocation (script_loc, line, column, ctx.scripts, frame ["location"]);
			Assert.Equal (function_name, frame ["functionName"].Value<string> ());

			await CheckLocalsOnFrame (frame, ctx, test_fn);
		}

		async Task CheckLocalsOnFrame (JToken frame, DebugTestContext ctx, Action<JToken> test_fn)
			=> test_fn (await GetProperties (frame ["callFrameId"].Value<string> (), ctx));

		async Task<JToken> CheckObjectOnFrameLocals (JToken frame, string name, DebugTestContext ctx, Action<JToken> test_fn)
		{
			//Console.WriteLine ($"GetObjectPropertiesFor frame: {frame["callFrameId"]} object name {name}");
			var locals = await GetProperties (frame ["callFrameId"].Value<string> (), ctx);
			//Console.WriteLine ($"GetPropertiesForObject: locals: {locals.ToString ()}");

			return await CheckObjectOnLocals (locals, name, ctx, test_fn);
		}

		async Task<JToken> CheckObjectOnLocals (JToken locals, string name, DebugTestContext ctx, Action<JToken> test_fn)
		{
			var obj = locals.Where (jt => jt ["name"]?.Value<string> () == name)// FIXME: just temporary, to handle valuetypes && jt ["value"]["type"]?.Value<string> () == "object")
					.FirstOrDefault ();
			if (obj == null)
				Assert.True (false, $"Could not find a var with name {name} and type object");

			var props = await GetProperties (obj ["value"]["objectId"].Value<string> (), ctx);
			if (test_fn != null) {
				try {
					test_fn (props);
				} catch (Exception) {
					Console.WriteLine ($"Failed for properties: {props}");
					throw;
				}
			}

			return props;
		}

		async Task<JToken> GetProperties (string id, DebugTestContext ctx)
		{
			Console.WriteLine ($"GetProperties id: {id}");
			var get_prop_req = JObject.FromObject (new {
				objectId = id
			});

			var frame_props = await ctx.cli.SendCommand ("Runtime.getProperties", get_prop_req, ctx.token);
			if (!frame_props.IsOk)
				Assert.True (false, $"Runtime.getProperties failed for {get_prop_req.ToString ()}");

			var locals = frame_props.Value ["result"];
			Console.WriteLine ($"** GetProperties got the result back: locals: {locals.ToString ()}");
			return locals;
		}

		async Task<Result> SetBreakpoint (string url_key, int line, int column, DebugTestContext ctx, bool expect_ok=true)
		{
			var bp1_req = JObject.FromObject(new {
				lineNumber = line,
				columnNumber = column,
				url = dicFileToUrl[url_key],
			});

			var bp1_res = await ctx.cli.SendCommand ("Debugger.setBreakpointByUrl", bp1_req, ctx.token);
			Assert.True (expect_ok ? bp1_res.IsOk : bp1_res.IsErr);

			return bp1_res;
		}

		//TODO add tests covering basic stepping behavior as step in/out/over
	}

	class DebugTestContext
	{
		public InspectorClient cli;
		public Inspector insp;
		public CancellationToken token;
		public Dictionary<string, string> scripts;

		public DebugTestContext (InspectorClient cli, Inspector insp, CancellationToken token, Dictionary<string, string> scripts)
		{
				this.cli = cli;
				this.insp = insp;
				this.token = token;
				this.scripts = scripts;
		}
	}

	static class TestExtensions
	{
		public static void HasNumber (this JToken locals, string name, int expected_val)
		{
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;
				var val = l["value"];
				Assert.Equal ("number", val ["type"]?.Value<string> ());
				Assert.Equal (expected_val, val["value"]?.Value <int> ());
				return;
			}
			Assert.True(false, $"Could not find variable '{name}'");
		}

		public static void HasString (this JToken locals, string name, string value)
		{
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;
				var val = l["value"];
				if (value == null) {
						Assert.Equal ("object", val ["type"]?.Value<string> ());
						Assert.Equal ("null", val["subtype"]?.Value<string> ());
				} else {
						Assert.Equal ("string", val ["type"]?.Value<string> ());
						Assert.Equal (value, val["value"]?.Value <string> ());
				}
				return;
			}
			Assert.True(false, $"Could not find variable '{name}'");
		}

		public static JToken HasObject (this JToken locals, string name, string class_name, string subtype=null)
		{
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;

				var val = l["value"];
				Console.WriteLine ($"-- HasObject: {val.ToString ()}");
				//FIXME:
				var type = val ["type"]?.Value<string> ();
				if (type != "object" && type != "valuetype")
					Assert.True (false, $"Expected an object or valuetype, but got {type}");
				//Assert.Equal ("object", val ["type"]?.Value<string> ());
				Assert.Equal (class_name, val ["className"]?.Value<string> ());
				Assert.Equal (subtype, val ["subtype"]?.Value<string> ());
				return l;
			}
			Assert.True(false, $"Could not find variable '{name}'");
			return null;
		}

		public static void HasValueType (this JToken locals, string name, string class_name) {
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;

				var val = l["value"];
				Console.WriteLine ($"-- HasObject: {val.ToString ()}");
				Assert.Equal ("valuetype", val ["type"]?.Value<string> ());
				Assert.Equal (class_name, val ["className"]?.Value<string> ());
				return;
			}
			Assert.True(false, $"Could not find variable '{name}'");
		}

		public static void HasArray (this JToken locals, string name, string class_name) {
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;

				var val = l["value"];
				Assert.Equal ("object", val ["type"]?.Value<string> ());
				Assert.Equal ("array", val ["subtype"]?.Value<string> ());
				Assert.Equal (class_name, val ["className"]?.Value<string> ());

				//FIXME: elements?
				return;
			}
			Assert.True(false, $"Could not find variable '{name}'");
		}

		public static void HasFunction (this JToken locals, string name, string description, string subtype=null) {
			Console.WriteLine ($"** Locals: {locals.ToString ()}");
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;

				var val = l["value"];
				Assert.Equal ("function", val ["type"]?.Value<string> ());
				Assert.Equal (description, val ["description"]?.Value<string> ());
				Assert.Equal (subtype, val ["subtype"]?.Value<string> ());
				return;
			}
			Assert.True(false, $"Could not find variable '{name}'");
		}

	}

	enum StepKind
	{
		Into,
		Over,
		Out
	}
}
