"""Dedicated short-lived draft test invocation, with a role that cannot access workload resources."""
import contextlib
import io
import json


class BoundedLog(io.StringIO):
    def write(self, value):
        return super().write(str(value)[:max(0, 2000-self.tell())])


def handler(event, context):
    source = event.get('source', '')
    if not isinstance(source, str) or len(source.encode()) > 8192:
        return {'passed': False, 'message': 'Código fuera del límite.'}
    output = BoundedLog()
    namespace = {'__name__': 'index'}
    try:
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            exec(compile(source, 'index.py', 'exec'), namespace)
            value = namespace['handler'](event.get('event', {}), context)
        encoded = json.dumps(value, ensure_ascii=False, default=str)
        return {'passed': True, 'output': encoded[:6000], 'logs': output.getvalue(),
                'message': 'Prueba ejecutada en Lambda aislada, sin permisos sobre tus recursos AWS.'}
    except Exception as error:
        return {'passed': False, 'output': '', 'logs': output.getvalue(),
                'message': 'Prueba fallida: ' + type(error).__name__ + '. Revisa el código y sus dependencias.'}
