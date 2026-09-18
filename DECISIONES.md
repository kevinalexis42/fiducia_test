# DECISIONES — Microservicio de Auditoría

## 1. Hallazgos en el código legacy

Durante la revisión del código legacy encontré los siguientes problemas. Los ordené según su impacto y, en cada caso, indico cómo quedaron en el microservicio.

| # | Problema | Ubicación | Estado en el microservicio |
|---|---|---|---|
| 1 | **Envío de correos dentro de `TransactionScope`.** El correo se envía antes de confirmar la transacción. Si `ts.Complete()` falla, se notifica una acción que finalmente no quedó registrada. Y si el SMTP tarda en responder, la transacción de Oracle mantiene los locks durante toda la espera. | `BAuditoria.Registrar` | **Resuelto** mediante Outbox (ver punto 3). |
| 2 | **SQL construido por concatenación.** Permite inyección en `usuario`, `entidad`, `claveEntidad` y los snapshots JSON. Un valor como `usuario=x' OR '1'='1` devuelve registros que no corresponden a la consulta. | `EAuditoria.Insertar/Buscar` | **Resuelto.** EF Core usa consultas parametrizadas. Hay una prueba de integración con ese mismo payload. |
| 3 | **Credenciales en el código.** Usuario, contraseña e IP de Oracle (`COREFID / Cor3F1d$2019 / 10.0.2.31`) en un `const` que usan unos 40 módulos. | `ADOracle` | **Resuelto.** Variables de entorno y, en Kubernetes, un `Secret`. Esa contraseña debería rotarse hoy, sin esperar a la migración. |
| 4 | **Un `catch` que devuelve `Ok("OK","Procesado")`.** Cualquier error se reporta como éxito. En un módulo cuya función es conservar evidencia, esto oculta la pérdida de registros. | `AuditoriaController.Registrar` | **Resuelto.** Los errores inesperados devuelven `500 E99`. Es la única desviación deliberada del comportamiento legacy (ver punto 2). |
| 5 | **`SELECT *` sin límite ni paginación.** Sin `fechaDesde`, la consulta trae toda la tabla. | `EAuditoria.Buscar` | **Parcial.** Índices sobre `(empresa, fecha DESC)` y `(empresa, usuario)`, y un límite configurable `Consulta:MaximoResultados`, desactivado por defecto para no alterar los resultados de los batch. Paginar requiere un contrato v2. |
| 6 | **Conexiones sin `using` ni `finally`.** Ante una excepción la conexión queda abierta y el pool se agota. | `EAuditoria` | **Resuelto por diseño.** `DbContext` con ciclo de vida scoped. |
| 7 | **El `out string error` se descarta en `Consultar`.** Una caída de la base de datos devuelve una lista vacía, indistinguible de "no hay datos". | `BAuditoria.Consultar` | **Resuelto.** La consulta propaga la excepción y el cliente recibe `500 E99`, nunca `[]`. |
| 8 | **`DateTime.Now`.** La hora depende del servidor y no se puede controlar en las pruebas. Complica los rangos entre zonas horarias y hace el handler no determinista. | `BAuditoria.Registrar` | **Resuelto.** `TimeProvider` inyectado y fechas en UTC con `timestamptz`. |
| 9 | **Regla de negocio en el controlador.** La combinación `BATCH` + `Q` que produce `E10` se evaluaba en el controlador, no en el dominio. Contradice el criterio de controladores delgados y es fácil perderla en una migración. | `AuditoriaController` | **Resuelto.** Vive en el agregado y se evalúa antes que las demás, igual que en el legacy. |
| 10 | **API sin versionar y `StackTrace` expuesto al cliente.** | `AuditoriaController` | La ruta **no se cambió** porque rompería a los consumidores. El `StackTrace` se reemplazó por un `traceId`. |

Otros puntos menores: `Canal` se acepta sin validar aunque el contrato lo define como enum; `ENotificacion` está acoplado con `new`; la secuencia `SQ01AUDITORIA.NEXTVAL` se consulta en un round-trip separado del `INSERT`.

## 2. Dónde vive cada regla y por qué

### Invariantes del registro

Las reglas `E01` a `E05`, `E10`, el valor por defecto `WEB` y el cálculo de la acción sensible están en `RegistroAuditoria.Registrar(...)`, el único punto autorizado para construir la entidad. Son propiedades del registro, no de la petición HTTP: si mañana el registro llega por RabbitMQ o por un batch, las mismas reglas aplican igual.

El orden de validación es el del legacy, primero `E10` y después `E01` a `E05`, para que el mismo cuerpo produzca el mismo código. `Accion`, `Canal`, `CodigoEmpresa` y `CodigoModulo` son value objects, así que no se puede crear una instancia inválida.

### Validación de formato

Las restricciones de formato (`maxLength`, el enum de `canal`) se validan en el DTO del API con DataAnnotations y responden `400 E00 "Petición inválida."`. El dominio no necesita saber que `codigoEmpresa` mide cuatro caracteres; eso es responsabilidad del contrato OpenAPI.

### Mapeo de códigos a HTTP

El controlador es el único componente que conoce HTTP. `E01` a `E05` responden `200` (no es lo más correcto, pero es lo que hace producción), `E10` y `E00` responden `400`, y `E99` responde `500`.

### Append-only

El agregado no expone setters ni métodos que modifiquen sus datos, y el repositorio solo ofrece `Agregar`. Los valores `U` y `D` de `accion` describen la acción que se está auditando, no una operación sobre el registro. Un registro de auditoría no se edita ni se elimina; la retención se gestiona con particiones por fecha, no con `DELETE`.

### CQRS

La escritura usa `RegistrarAuditoriaCommand`, que pasa por el agregado y el Unit of Work. La lectura usa `ConsultarAuditoriaQuery`, que consulta directamente los DTO con `AsNoTracking`, sin cargar el agregado. Por ahora la separación aporta un beneficio moderado; será más evidente cuando la reportería sobre millones de registros compita con las escrituras fila a fila en Oracle.

No uso MediatR. Dos handlers no justifican una dependencia que además cambió a licencia comercial. Definí `ICommandHandler<,>` e `IQueryHandler<,>` propios en Application y los registré en DI.

### Única desviación de comportamiento

En el legacy, un fallo interno durante el registro devuelve `200 OK "Procesado"`. En el microservicio devuelve `500 E99`. El código `E99` ya existe en el contrato y los consumidores lo manejan en `consulta`. Mantener el error oculto habría sido reproducir el defecto 4.

## 3. Qué resuelve el patrón Outbox en este caso

En `BAuditoria.Registrar`, el legacy ejecuta el `INSERT` y después `EnviarCorreo` dentro del mismo `TransactionScope`. Esto genera dos problemas concretos: si el correo sale y `ts.Complete()` falla, Seguridad recibe una alerta sobre una acción que nunca quedó registrada; y si el SMTP tarda 30 segundos, la transacción de Oracle permanece abierta con locks sobre `T01AUDITORIA` mientras los tres canales siguen insertando.

Con el Outbox, el command solo escribe en la base de datos. La auditoría y el mensaje pendiente se guardan dentro de la misma transacción:

```text
UnitOfWork.CommitAsync
    ├── BeginTransaction
    ├── Insertar auditoría
    ├── El agregado confirma y emite AuditoriaRegistrada con su id
    ├── Insertar mensaje en outbox
    └── Commit
```

Si el commit falla, no queda ni el registro ni el evento. Si se completa, el evento queda almacenado y se publicará. La prueba `Si_falla_el_insert_del_outbox_tampoco_queda_la_auditoria` verifica este comportamiento forzando un fallo en el insert del Outbox mediante un interceptor. El envío del correo deja de ser parte del camino transaccional; será responsabilidad de un consumidor del evento.

El `OutboxDispatcher`, un `BackgroundService`, obtiene los pendientes con `FOR UPDATE SKIP LOCKED` (varias réplicas sin procesar el mismo mensaje), publica, y marca `processed_at`. Si falla, incrementa `attempts`, guarda el detalle en `error` y aplica un backoff exponencial de 5 a 300 segundos. La caída del broker no afecta al API ni a `/readyz`; lo verifiqué deteniendo RabbitMQ. La entrega es at-least-once, así que el consumidor debe deduplicar por `EventId`, que viaja como `MessageId` de AMQP.

## 4. Plan de migración de `/api/auditoria/**`

**Validación previa.** Shadow traffic durante una semana en QA, comparando monolito y microservicio en código HTTP, cuerpo y conteo de registros por día. El criterio de aborto queda por escrito antes de empezar: 5xx por encima del 0,5 % durante cinco minutos, p95 superior a 300 ms, más de 500 mensajes pendientes en el Outbox, o más del 0,1 % de diferencia en el conteo de registros por hora.

**Canary progresivo.** Con el `HTTPRoute` de `deploy/k8s/20-httproute-canary.yaml`: 10 % al microservicio y 90 % al monolito durante 24 horas; después 25 %, 50 % y 100 %. Cada cambio es un merge en GitOps y se revisa con las mismas métricas.

**Qué se mira.** Tasa de errores y latencia de cada backend en el gateway; pendientes y mensajes con `attempts > 3` en `outbox_messages`; conteo de registros por hora en ambos lados y un checksum de `(empresa, usuario, entidad, clave, accion)`. Un `200` no garantiza nada: el monolito registra hora de Quito y el servicio UTC, y si los rangos no se normalizan aparecen diferencias en las consultas con códigos HTTP correctos. La comparación de datos las detecta antes de que las vean los consumidores.

**Rollback.** Un PR en GitOps que pone el peso del microservicio en `0`. Segundos, sin redeploy y sin tocar el monolito. Durante un incidente la prioridad es detener el problema y después investigar: si a las 3 de la mañana aparecen registros duplicados, primero peso a `0`, luego se analiza.

**Convivencia.** Durante el canary ambos escriben en la misma tabla Oracle; los registros se complementan porque cada petición la atiende uno de los dos. Antes de pasar del 10 % hay que resolver dos cosas: `SQ01AUDITORIA.NEXTVAL` debe seguir siendo la fuente del `id` en el microservicio (EF con `UseSequence`), y las fechas deben guardarse en UTC y convertirse al leer, o los consumidores verán diferencias de cinco horas.

## 5. Qué queda pendiente y qué haría con dos días más

- **Oracle real.** PostgreSQL es un sustituto. Falta el provider `Oracle.EntityFrameworkCore`, el mapeo a `T01AUDITORIA` y `UseSequence("SQ01AUDITORIA")`.
- **Consumidor del evento** que envíe el correo a Seguridad, con deduplicación por `EventId`, manejo de errores y una Dead Letter Queue.
- **Paginación y `fechaHasta`** en un contrato v2 (`/api/v1/auditoria`), conviviendo con el actual en el gateway y con un plan de deprecación para móvil, BPM y batch.
- **Retención.** Particionado mensual por `fecha_registro` y un proceso de archivado. Hoy solo hay índices.
- **Pipeline y despliegue.** CI con `dotnet test`, build y escaneo de la imagen; Helm chart en lugar de YAML plano; NetworkPolicies y `ServiceMonitor`.
- **OpenTelemetry** (P2, punto 14). Trazas de ASP.NET Core y Npgsql, métricas de runtime exportadas por OTLP al collector del cluster, y métricas propias `outbox_pendientes` y `outbox_attempts`, que son las que alimentan el criterio de aborto del canary. El `traceId` W3C ya se propaga en los logs con `Activity.Current`, así que la instrumentación entra sin tocar el código de negocio.

## 6. Uso de IA durante el desarrollo

Usé **Claude Code** como asistente de pair programming. Antes de escribir código partí de una especificación formada por este documento y el plan de capas. Con su ayuda generé el esqueleto de la solución, las configuraciones de EF, el dispatcher, los manifiestos de Kubernetes y la primera versión de las pruebas. Aceleró la implementación, pero hubo varias cosas que tuve que revisar y corregir.

**Dispatcher y reintentos de EF Core.** El dispatcher abría una transacción manual con `EnableRetryOnFailure` habilitado. Compilaba y las pruebas pasaban porque el factory de tests reemplazaba el `DbContext` sin la configuración de reintentos, pero fallaba en el `docker compose` real. Lo detecté en el smoke test; envolví la transacción en `CreateExecutionStrategy()` y alineé el factory con la configuración real.

**Mapeo de `CodigoModulo`.** Se había definido como `record struct`. EF no puede mapear un struct a una columna nullable y la migración generó una columna `NOT NULL`. Lo cambié a un `record` de referencia con un `Desde()` que puede devolver `null`.

**Proyección LINQ.** Accedía a `.Valor` de los value objects dentro del `Select`, y esa expresión no se traduce a SQL. Ahora se materializa y se mapea en memoria.

**Pruebas del Outbox.** Reiniciaban el `FakeTimeProvider` hacia atrás, lo que lanza una excepción. Las reescribí con tiempos relativos al momento actual del reloj.

**Helper de tokens.** Generaba un `nbf` posterior a `exp` en el caso del token expirado, y la librería lo rechazaba antes de llegar al API.

**Organización del código.** La primera estructura tendía a un archivo por interfaz, incluso con seis u ocho líneas; había un `RegistroAuditoriaResponse` que duplicaba el DTO de Application y OpenTelemetry repartido en cinco paquetes. Agrupé los puertos, la seguridad y las configuraciones de EF por tema, eliminé el duplicado y dejé OpenTelemetry documentado como P2. Quedó una solución con menos piezas que explicar y mantener, con el mismo comportamiento y las mismas 57 pruebas.
