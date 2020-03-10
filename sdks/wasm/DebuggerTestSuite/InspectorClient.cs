using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

using System.Net.WebSockets;
using System.Threading;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace WebAssembly.Net.Debugging {
	internal class InspectorClient : DevToolsClient {
		List<(int, TaskCompletionSource<Result>)> pending_cmds = new List<(int, TaskCompletionSource<Result>)> ();
		Func<string, JObject, CancellationToken, Task> onEvent;
		int next_cmd_id;

		public InspectorClient (ILogger logger) : base(logger) {}

		Task HandleMessage (string msg, CancellationToken token)
		{
			var res = JObject.Parse (msg);
			if (res ["id"] == null)
				DumpProtocol (string.Format("Event method: {0} params: {1}", res ["method"], res ["params"]));
			else
				DumpProtocol (string.Format ("Response id: {0} res: {1}", res ["id"], res));

			if (res ["id"] == null)
				return onEvent (res ["method"].Value<string> (), res ["params"] as JObject, token);
			var id = res ["id"].Value<int> ();
			var idx = pending_cmds.FindIndex (e => e.Item1 == id);
			if (idx < 0) {
				Console.WriteLine ($"-- couldn't find a pending cmd");
				foreach (var pc in pending_cmds)
					Console.WriteLine ($"\tpc: {pc.Item1}");
				throw new ArgumentException ($"Cannot find a pending command with id: {id}");
			} else {
				Console.WriteLine ($"HandleMessage: For id {id}, got msg: {msg}, removing the pending cmd");
			}

			var item = pending_cmds [idx];
			pending_cmds.RemoveAt (idx);
			item.Item2.SetResult (Result.FromJson (res));
			return null;
		}

		public async Task Connect(
			Uri uri,
			Func<string, JObject, CancellationToken, Task> onEvent,
			Func<CancellationToken, Task> send,
			CancellationToken token) {

			this.onEvent = onEvent;
			await ConnectWithMainLoops (uri, HandleMessage, send, token);
		}

		public Task<Result> SendCommand (string method, JObject args, CancellationToken token)
		{
			int id = ++next_cmd_id;
			if (args == null)
				args = new JObject ();

			var o = JObject.FromObject (new {
				id = id,
				method = method,
				@params = args
			});

			var tcs = new TaskCompletionSource<Result> ();
			pending_cmds.Add ((id, tcs));

			var str = o.ToString ();
			//Log ("protocol", $"SendCommand: id: {id} method: {method} params: {args}");
			Console.WriteLine ($"SendCommand: id: {id} method: {method} params: {args}");

			var bytes = Encoding.UTF8.GetBytes (str);
			Send (bytes, token);
			return tcs.Task;
		}

		protected virtual void DumpProtocol (string msg){
			// Console.WriteLine (msg);
			//XXX make logging not stupid
		}
	}
} 
