using System;
using System.IO;
using System.Net;
using System.Text;
using System.Collections;

class Program {
    static Dictionary<string, HashSet<string>> SESSIONS = new();
    //static Dictionary<string, Dictionary<string, string>> SESSIONS = new();
    static Dictionary<string, string> LOCATIONS = new();
    /*
    static Dictionary<string, string> LOCATIONS = new Dictionary<string, string>
    {
        { "mustahattu", "/home/mustahattu/"},
        { "Sync"      , "/home/mustahattu/Sync/"},
        { "Testing"   , "/home/mustahattu/Sync/Code/C-sharp/file-manager/"},
        { "root", "/" },
    };
    */

    static void read_locations() {
        string path = "locations.txt";

        foreach (var line in File.ReadLines(path))
        {
            // Skip empty lines
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Split at the first '='
            int index = line.IndexOf('=');
            if (index < 0) continue; // Skip lines without '='

            string key = line.Substring(0, index).Trim();
            string value = line.Substring(index + 1).Trim();

            LOCATIONS[key] = value; // Add or update
        }

        // Test
        foreach (var kv in LOCATIONS)
        {
            Console.WriteLine($"{kv.Key} => {kv.Value}");
        }
    }

    static int Main(string[] args) {
        read_locations();

        foreach(var entry in LOCATIONS ) {
            if (!Directory.Exists(entry.Value)) {
                Console.WriteLine("Base Directory not found.");
                return -1;
            }
        }

        // Start a simple HTTP listener
        HttpListener listener = new HttpListener();
        //listener.Prefixes.Add("http://localhost:5000/");
        listener.Prefixes.Add("http://+:5000/");
        listener.Start();

        while(true) {
            Console.WriteLine("============================");
            var context = listener.GetContext(); // wait for request
            var response = context.Response;
            var request = context.Request;
            string session_id = GetOrCreateSession(context);

            if (context.Request.Url?.AbsolutePath == "/") {
                handle_locations_path(context);
                continue;
            }

            // Sync/foo/bar
            string absolute_link_path = Uri.UnescapeDataString(request.Url?.AbsolutePath);
                absolute_link_path = absolute_link_path.Trim('/');

            // parts = [ Sync, foo/bar ]
            string[] parts = absolute_link_path.Split('/', 2); 

            // location = Sync
            string location = parts[0]; 
            // remaining = foo/bar
            string relativePath = parts.Length > 1 ? parts[1] : ""; 

            Console.WriteLine("SESSION_ID : {0}", session_id );
            switch( location ) {
                case "select":
                    handle_select(session_id,context);
                    continue;
                case "copy":
                    handle_copy(session_id,context);
                    continue;
                case "delete":
                    handle_delete(session_id,context);
                    continue;
                case "mkdir":
                    handle_mkdir(session_id,context);
                    continue;
            }
            Console.WriteLine("Absolute Link Path: {0}", absolute_link_path );
            Console.WriteLine("Parts : [{0}]" ,string.Join(",",parts) );
            Console.WriteLine("Location : {0}" , location );
            Console.WriteLine("Relative Path : {0}" , relativePath );


            if( !LOCATIONS.ContainsKey(location) ) {
                handle_not_found(response);
                continue;
            }

            // path = /home/mustahattu/Sync/ + foo/bar
            string path = LOCATIONS[location] + relativePath;
                Console.WriteLine("Final Path : {0}" , path );
            serve_path(session_id,response,path,  absolute_link_path );
        }
        return 0; 
    }
    static void handle_locations_path(HttpListenerContext context) {
        string[] dir_names = LOCATIONS.Keys.ToArray();

        var html = new StringBuilder();
        html.Append($@"
        <!DOCTYPE html>
        <html>
            <head>
                <title>Index</title>
                <style>{STYLE}</style> 
            </head>
        <body>
            <header>Locations </header>
            <div class=""files"" style=""font-size: 1.3rem;""
            <ul>
        ");
            string[] location_names = LOCATIONS.Keys.ToArray();
            foreach (var location_name in location_names) {
                html.Append($@"
                <li><b>[Location]</b> <a href='/{location_name}'>{location_name}</a></li>
                ");
            }
        html.Append(@"
            </ul>
            </div>
            </body>
        </html>
        ");

        byte[] buffer = Encoding.UTF8.GetBytes(html.ToString());
        context.Response.ContentType = "text/html; charset=UTF-8";
        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
        context.Response.Close();
    }
    static void handle_mkdir(string session_id, HttpListenerContext ctx) {
        using var reader = new System.IO.StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
        string body = reader.ReadToEnd().Trim('+');

        var formData = System.Web.HttpUtility.ParseQueryString(body);
        string currentPath = formData["currentPath"];
        string curr_path_fs = link_to_fs_path(currentPath); 

        string dir_name = formData["dir_name"];
        string fs_dir_path = $"{curr_path_fs}/{dir_name}";

        Console.WriteLine($"mkdir dir_name: {dir_name}");
        Console.WriteLine($"mkdir currentPath: {currentPath}");
        Console.WriteLine($"mkdir fs_dir_path: {fs_dir_path}");
        Console.WriteLine($"dir_name: {dir_name}");

        Directory.CreateDirectory(fs_dir_path);

        ctx.Response.StatusCode = 302;
        ctx.Response.RedirectLocation = currentPath;
        ctx.Response.Close();
    }
    static void handle_delete(string session_id, HttpListenerContext ctx) {
        using var reader = new System.IO.StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
        string body = reader.ReadToEnd();

        var formData = System.Web.HttpUtility.ParseQueryString(body);
        string currentPath = formData["currentPath"];
        foreach (var elem in SESSIONS[session_id] )
        {
            string fs_path = link_to_fs_path(elem);
            DeletePath(fs_path);
            Console.WriteLine($"Deleting {fs_path}");
        }
        SESSIONS[session_id].Clear();

        ctx.Response.StatusCode = 302;
        ctx.Response.RedirectLocation = currentPath;
        ctx.Response.Close();
    }

    static void handle_copy(string session_id, HttpListenerContext ctx) {
        using var reader = new System.IO.StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
        string body = reader.ReadToEnd();

        // Parse form data
        var formData = System.Web.HttpUtility.ParseQueryString(body);

        string currentPath = formData["currentPath"];

        string curr_path_fs = link_to_fs_path(currentPath); 
        foreach (var elem in SESSIONS[session_id] )
        {
            string fs_path = link_to_fs_path(elem);
            var name = Path.GetFileName(fs_path);
            string dest_fs_path = curr_path_fs + "/" + name;
            CopyPath(fs_path,dest_fs_path);
            Console.WriteLine($"Copying {fs_path} to {dest_fs_path}");
        }
        SESSIONS[session_id].Clear();

        ctx.Response.StatusCode = 302;
        ctx.Response.RedirectLocation = currentPath;
        ctx.Response.Close();
    }

    static string link_to_fs_path(string path) {
        string[] parts = path.Trim('/').Split('/', 2); 

        // location = Sync
        string location = parts[0]; 
        // remaining = foo/bar
        string relative_link_path = parts.Length > 1 ? parts[1] : ""; 

        string fs_path = LOCATIONS[location] + relative_link_path;

        return fs_path;
    }

    static void handle_select(string session_id, HttpListenerContext ctx) {
        using var reader = new System.IO.StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
        string body = reader.ReadToEnd();

        // Parse form data
        var formData = System.Web.HttpUtility.ParseQueryString(body);

        string currentPath = formData["currentPath"];

        var selections = formData.GetValues("selected") ?? Array.Empty<string>();

        SESSIONS[session_id].Clear();
        Console.WriteLine("Selected items:");
        foreach (var item in selections)
        {
            Console.WriteLine($" - {currentPath}/{item}");
            SESSIONS[session_id].Add($"{currentPath}/{item}");
        }
        foreach (var elem in SESSIONS[session_id] )
        {
            Console.WriteLine("element of set: {0}",elem);
        }


        // Redirect back to the original directory
        ctx.Response.StatusCode = 302;
        ctx.Response.RedirectLocation = currentPath;
        ctx.Response.Close();
    }

    static void handle_not_found(HttpListenerResponse response) {
        response.StatusCode = 404;
        byte[] buffer = Encoding.UTF8.GetBytes("Not found");
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.Close();
    }

    static void serve_path( string session_id, HttpListenerResponse response, string path , string absolute_link_path ) {
        if( Directory.Exists(path) ) {
            string[] dirs = null;
            string[] files = null;
            try {
                dirs = Directory.GetDirectories(path);
                files = Directory.GetFiles(path);
            }
            catch (Exception ex) {
                response.StatusCode = 500;
                byte[] buffer = Encoding.UTF8.GetBytes($"Error: {ex.Message}");
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.Close();
                return;
            }

            var html = new StringBuilder();
            html.Append($@"
            <!DOCTYPE html>
            <html>
                <head>
                    <title>Index</title>
                    <style>{STYLE}</style>
                </head>
            <body>
            <header> Index of {absolute_link_path} </header>
            <div class=""buttons"">"
            );

            if (string.IsNullOrEmpty(absolute_link_path)) {
                throw new Exception("PANIC");
            }
            html.Append($@"
                <input form=""selectForm"" type= ""submit"" value=""Select"" >

                <form method= ""post"" action=""/copy/"" >
                    <input type= ""hidden"" name=""currentPath"" value='/{absolute_link_path}'>
                    <input type=""submit"" value=""Copy Here "">
                </form>

                <form method=""post"" action=""/delete/"" >
                    <input type=""hidden"" name=""currentPath"" value='/{absolute_link_path}'>
                    <input type=""submit"" value=""Delete Selected Files "">
                </form>
                
                <form method=""post"" action= ""/mkdir/"" >
                    <label for= ""dir_name"" >Make new directory: </label>
                    <input type= ""text"" name="" dir_name"" >
                    <input type= ""hidden"" name=""currentPath"" value='/{absolute_link_path}'>
                    <button type= ""submit"" >Submit</button>
                </form>
            </div>
            <div class=""files"">
                <form method= ""post"" action= ""/select/"" id=""selectForm"" >
                    <input type= ""hidden"" name= ""currentPath"" value='/{absolute_link_path}'>
                <li><a href='/{absolute_link_path}/..'>[..]</a></li>"
            );
            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                var link = $"/{absolute_link_path}/{name}";


                string selected = SESSIONS[session_id].Contains(link) ? "checked" : "" ;

                link = Uri.EscapeUriString(link);

                html.Append($@"
                <li>
                    <input type=""checkbox"" name=""selected"" value=""{name}"" {selected} >
                    <b>[DIR]</b> <a href='{link}'>{name}</a>
                </li>
                ");
            }

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var link = $"/{absolute_link_path}/{name}";

                string selected = SESSIONS[session_id].Contains(link) ? "checked" : "" ;

                link = Uri.EscapeUriString(link);

                html.Append($@"
                <li>
                    <input type=""checkbox"" name=""selected"" value=""{name}"" {selected} >
                    <a href='{link}'>{name}</a>
                </li>
                ");
            }

            html.Append($@"
                </form>
            </div>
            </body>
            </html>
            ");

            byte[] buff = Encoding.UTF8.GetBytes(html.ToString());
            response.ContentType = "text/html; charset=UTF-8";
            response.OutputStream.Write(buff, 0, buff.Length);
            response.Close();

        } else 
        if( File.Exists(path) ) {
            try {
                using var fs = File.OpenRead(path);
                response.ContentLength64 = fs.Length;
                response.ContentType = "application/octet-stream";
                fs.CopyTo(response.OutputStream);
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 32 || ex.ErrorCode == 995)
            {
                // ErrorCode 32 = broken pipe, 995 = I/O aborted
                Console.WriteLine("Client disconnected while streaming file.");
            }
            catch (Exception ex) {
                response.StatusCode = 500;
                byte[] buffer = Encoding.UTF8.GetBytes($"Error: {ex.Message}");
                try { response.OutputStream.Write(buffer, 0, buffer.Length); } catch {}
            }
            finally {
                response.OutputStream.Close();
                response.Close();
            }
        } else {
            handle_not_found(response);
        }
    }

    static string GetOrCreateSession(HttpListenerContext context)
    {
        string sessionId = null;

        if (context.Request.Cookies["SESSION_ID"] != null) {
            sessionId = context.Request.Cookies["SESSION_ID"].Value;
            if( !SESSIONS.ContainsKey(sessionId) ) {
                SESSIONS[sessionId] = new HashSet<string>();
            }
        } else {
        //if (string.IsNullOrEmpty(sessionId) || !SESSIONS.ContainsKey(sessionId)) {
            sessionId = Guid.NewGuid().ToString();
            SESSIONS[sessionId] = new HashSet<string>();

            var cookie = new Cookie("SESSION_ID", sessionId) {
                Path = "/",
                HttpOnly = true
            };
            context.Response.Cookies.Add(cookie);
        }

        return sessionId;
    }
    static void DeletePath(string fs_path) {
        if( Directory.Exists(fs_path) ) {
            Directory.Delete(fs_path, recursive: true);
        } else 
        if( File.Exists(fs_path) ) {
            File.Delete(fs_path);
        } 
    }

    static void CopyPath(string source_path, string destination_path) {
        if( Directory.Exists(source_path) ) {
            CopyDirectory(source_path,destination_path);
        } else 
        if( File.Exists(source_path) ) {
            File.Copy(source_path, destination_path, overwrite: true);
        }
    }

    static void CopyDirectory(string source_dir, string destination_dir)
    {
        // Prevent copying into itself or a subdirectory
        if (destination_dir.StartsWith(source_dir, StringComparison.OrdinalIgnoreCase)) {
            Console.WriteLine($"CopyDirectory: Cannot copy a directory into itself or its subdirectory. ABORTING ({source_dir} => {destination_dir}");
            return;
        }

        Directory.CreateDirectory(destination_dir);

        // Copy all files
        foreach (var file in Directory.GetFiles(source_dir))
        {
            string destFile = Path.Combine(destination_dir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        // Recursively copy subdirectories
        foreach (var dir in Directory.GetDirectories(source_dir))
        {
            string destSubDir = Path.Combine(destination_dir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }

    static string STYLE = @"
        .buttons {
            display: flex;
            gap: 10px;
            padding: 10px;
        }
        .files {
            padding-left: 2rem;
            padding-top: 1rem;
            list-style-type: none;
        }
        .files li {
            margin-bottom: 3px; 
        }
        html, body {
            height: 100%;
            margin: 0;
        }
        a {
            color: #4da3ff;
            text-decoration: none; 
        }
        body {
            font-family: Arial, sans-serif;
            background: #121212;
            padding: 0;
            color: #e0e0e0;
        }
        header {
            background: #1f1f1f;
            color: #ffffff;
            padding: 1.5rem;
            text-align: left;
            font-size: 1.5rem;
            font-weight: bold;
            box-shadow: 0 2px 6px rgba(0,0,0,0.5);
        }
        main {
            flex: 1;
            max-width: 800px;
            margin: 2rem auto;
            padding: 0 1rem;
        }
        .dir {
            background: #1e1e1e;
            margin: 1rem 0;
            border-radius: 8px;
            box-shadow: 0 2px 5px rgba(0,0,0,0.4);
            padding: 1rem 1.5rem;
            transition: transform 0.1s ease, box-shadow 0.1s ease, background 0.2s ease;
            cursor: pointer;
        }
        .dir:hover {
            transform: translateY(-2px);
            box-shadow: 0 4px 8px rgba(0,0,0,0.6);
            background: #2a2a2a;
        }
        .title {
            font-weight: bold;
            font-size: 1.1rem;
            color: #4da3ff;
        }
        .desc {
            font-size: 0.9rem;
            color: #bbbbbb;
            margin-top: 0.3rem;
        }
    ";
}

