"""Local copies of common sign-in page types, one site per host name (*.localhost resolves to 127.0.0.1 in browsers).

argv: port, passwords.json ({host: password}), log file. The log gets "page <host><path>" and
"submit host=<host> user=<login> ok=<bool>" lines (never a password).
"""
import datetime
import json
import secrets
import sys
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT, PASSWORDS, LOG = int(sys.argv[1]), sys.argv[2], sys.argv[3]
STYLE = "<style>body{font-family:Segoe UI,Arial;margin:40px} input{display:block;margin:8px 0 14px;padding:8px;width:280px} .btn{padding:8px 18px;cursor:pointer}</style>"


def log(line):
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S.%f")[:-3] + " " + line + "\n")


def expected(host):
    with open(PASSWORDS, encoding="utf-8") as f:
        return json.load(f).get(host)


def rand():
    return secrets.token_urlsafe(12)


def page(title, body):
    return f"<!doctype html><html><head><meta charset=utf-8><title>{title}</title>{STYLE}</head><body>{body}</body></html>"


def keycloak(error=""):
    action = f"/login-actions/authenticate?session_code={rand()}&execution={rand()}&tab_id={rand()}"
    err = f'<p style="color:#b00">{error}</p>' if error else ""
    return page("Sign in to Corp", f"""<h2>Sign in</h2>{err}<form action="{action}" method="post">
<label for="username">Username or email</label><input id="username" name="username" autocomplete="username" autofocus>
<label for="password">Password</label><input id="password" name="password" type="password" autocomplete="current-password">
<input class="btn" type="submit" value="Sign In"></form>""")


def big():
    menu = "".join(f'<li><a href="#m{i}">Раздел {i}</a><ul>' + "".join(f'<li><a href="#s{i}_{j}">Пункт {j}</a></li>' for j in range(12)) + "</ul></li>" for i in range(40))
    form = """<form method="post" action="/login"><h2>Вход</h2><label for="lg">Логин</label><input id="lg" name="login">
<label for="pw">Пароль</label><input id="pw" name="pass" type="password"><button class="btn">Войти</button></form>"""
    return page("Банк — вход", f"<nav><ul>{menu}</ul></nav>{form}")


SPA = """<div id=app>Загрузка…</div><script>
setTimeout(function(){
  document.getElementById('app').innerHTML = '<input id="login" placeholder="Phone or email"><input id="pass" type="password" placeholder="Password"><div class="btn" role="button" id="go">Log in</div><p id="msg"></p>';
  function send(){ fetch('/api/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({user:document.getElementById('login').value,password:document.getElementById('pass').value})})
    .then(function(r){return r.json();}).then(function(r){ if(r.ok){history.pushState({},'','/account');document.title='My account';document.getElementById('app').innerHTML='<h1>Hello</h1>';} else document.getElementById('msg').textContent='Wrong'; }); }
  document.getElementById('go').onclick=send;
  document.getElementById('pass').addEventListener('keydown',function(e){if(e.key==='Enter')send();});
},1200);
</script>"""


class Handler(BaseHTTPRequestHandler):
    def host(self):
        return (self.headers.get("Host") or "").split(":")[0].lower()

    def send_html(self, html, status=200):
        body = html.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def redirect(self, to):
        self.send_response(302)
        self.send_header("Location", to)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def form(self):
        raw = self.rfile.read(int(self.headers.get("Content-Length", 0))).decode("utf-8")
        if self.headers.get("Content-Type", "").startswith("application/json"):
            return json.loads(raw or "{}")
        return {k: v[0] for k, v in urllib.parse.parse_qs(raw).items()}

    def check(self, user, password):
        ok = password == expected(self.host())
        log(f"submit host={self.host()} user={user} ok={ok}")
        return ok

    def do_GET(self):
        host, path = self.host(), urllib.parse.urlsplit(self.path).path
        log(f"page {host}{path}")
        if host == "sso-corp.localhost" and path == "/auth":
            return self.redirect(f"/login-actions/authenticate?session_code={rand()}&execution={rand()}&tab_id={rand()}")
        if host == "sso-corp.localhost" and path.startswith("/login-actions/"):
            return self.send_html(keycloak())
        if host == "mail-box.localhost" and path == "/signin":
            return self.send_html(page("Sign in - MailBox", '<form method="post" action="/signin/identifier"><input type="email" name="identifier" aria-label="Email or phone" autocomplete="username"><button class="btn">Next</button></form>'))
        if host == "mail-box.localhost" and path.startswith("/signin/challenge/pwd"):
            user = urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query).get("u", [""])[0]
            return self.send_html(page("Sign in - MailBox", f'<p>{user}</p><form method="post" action="/signin/challenge/pwd?u={urllib.parse.quote(user)}&TL={rand()}"><input type="password" name="Passwd" aria-label="Enter your password"><button class="btn">Next</button></form>'))
        if host == "shop-spa.localhost" and path in ("/", "/account"):
            return self.send_html(page("Shop", SPA))
        if host == "bank-big.localhost" and path == "/login":
            return self.send_html(big())
        if host == "frame-site.localhost" and path == "/":
            return self.send_html(page("Frame Site", '<iframe src="/widget" style="width:420px;height:320px"></iframe>'))
        if host == "frame-site.localhost" and path == "/widget":
            return self.send_html(page("widget", '<form method="post" action="/widget" target="_top"><label for="u">Login</label><input id="u" name="u" autocomplete="username"><label for="p">Password</label><input id="p" name="p" type="password"><button class="btn">Sign in</button></form>'))
        self.send_html(page("Not found", "404"), 404)

    def do_POST(self):
        host, path = self.host(), urllib.parse.urlsplit(self.path).path
        d = self.form()
        if host == "sso-corp.localhost":
            if self.check(d.get("username", ""), d.get("password", "")):
                return self.send_html(page("Corp Portal", "<h1>Portal</h1>"))
            return self.send_html(keycloak("Invalid username or password."))
        if host == "mail-box.localhost" and path == "/signin/identifier":
            return self.redirect(f"/signin/challenge/pwd?u={urllib.parse.quote(d.get('identifier', ''))}&TL={rand()}")
        if host == "mail-box.localhost":
            user = urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query).get("u", [""])[0]
            return self.send_html(page("Inbox", "<h1>Inbox</h1>") if self.check(user, d.get("Passwd", "")) else page("Sign in - MailBox", "Wrong"))
        if host == "shop-spa.localhost" and path == "/api/login":
            body = json.dumps({"ok": self.check(d.get("user", ""), d.get("password", ""))}).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        if host == "bank-big.localhost":
            return self.send_html(page("Счета", "<h1>Счета</h1>") if self.check(d.get("login", ""), d.get("pass", "")) else page("Банк — вход", "Неверный пароль"))
        if host == "frame-site.localhost":
            return self.send_html(page("Frame Site - Home", "<h1>Home</h1>") if self.check(d.get("u", ""), d.get("p", "")) else page("Frame Site", "Wrong"))
        self.send_html(page("Not found", "404"), 404)

    def log_message(self, *args):
        pass


ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
