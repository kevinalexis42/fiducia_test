# DECISIONES — Microservicio de Auditoría

## 1. Hallazgos en el código legacy

Ordenados por impacto. Indico cómo quedó cada uno en el microservicio.

| # | Problema | Dónde | Estado |
|---|---|---|---|
| 1 | **Correo dentro del `TransactionScope`.** Si `ts.Complete()` falla, ya se notificó una acción que no quedó registrada; si el SMTP tarda, Oracle retiene los locks toda la espera. | `BAuditoria.Registrar` | **Resuelto** con Outbox (§3). |
| 2 | **SQL por concatenación.** Inyección en `usuario`, `entidad`, `claveEntidad` y los snapshots JSON; `usuario=x' OR '1'='1` devuelve toda la tabla. | `EAuditoria.Insertar/Buscar` | **Resuelto.** EF Core parametriza; hay prueba de integración con ese payload. |
| 3 | **Credenciales en el código** (`COREFID / Cor3F1d$2019 / 10.0.2.31`) en un `const` usado por ~40 módulos. | `ADOracle` | **Resuelto.** Variables de entorno; en K8s, `Secret`. Esa contraseña hay que rotarla hoy, sin esperar la migración. |
| 4 | **`catch` que devuelve `Ok("OK","Procesado")`.** Un fallo se reporta como éxito: pérdida silenciosa de evidencia en el módulo que existe para no perderla. | `AuditoriaController.Registrar` | **Resuelto.** Fallo inesperado → `500 E99` (única desviación deliberada, §2). |
| 5 | **`SELECT *` sin límite.** Sin `fechaDesde` trae la tabla entera. | `EAuditoria.Buscar` | **Parcial.** Índices `(empresa, fecha DESC)` y `(empresa, usuario)`; límite configurable `Consulta:MaximoResultados`, apagado por defecto para no alterar a los batch. Paginar exige contrato v2. |
| 6 | **Conexiones sin `using`/`finally`**: en excepción no se cierran, fuga del pool. | `EAuditoria` | Resuelto por diseño (`DbContext` scoped). |
| 7 | **`out string error` descartado** en `Consultar`: una caída de BD devuelve `[]`, indistinguible de "sin datos". | `BAuditoria.Consultar` | **Resuelto.** La query lanza; el cliente recibe `500 E99`, nunca `[]`. |
| 8 | **`DateTime.Now`**: hora local del servidor, no inyectable; rompe rangos multi-zona y hace el handler no determinista. | `BAuditoria.Registrar` | **Resuelto.** `TimeProvider` inyectado, `timestamptz` UTC. |
| 9 | **Regla de negocio en el controlador** (`BATCH`+`Q` → `E10`). Es la más fácil de perder al migrar. | `AuditoriaController` | **Resuelto.** Vive en el agregado, evaluada primero, como en el legacy. |
| 10 | **API sin versionar** y **`StackTrace` al cliente** en el 500. | `AuditoriaController` | Ruta no cambiada (rompería consumidores); `StackTrace` sustituido por `traceId`. |

Menores: `Canal` se acepta sin validar aunque el contrato lo define como enum; `ENotificacion` acoplado por `new`; `SQ01AUDITORIA.NEXTVAL` se lee en un round-trip separado del `INSERT`.

## 2. Dónde vive cada regla y por qué

**Invariantes del registro** (`E01`–`E05`, `E10`, canal por defecto `WEB`, cálculo de acción sensible): en `RegistroAuditoria.Registrar(...)`, el único camino para construir la entidad. Son propiedades del registro, no de la petición HTTP: si mañana llega por RabbitMQ o por batch, aplican igual. Se evalúan en el mismo orden que el legacy (`E10` y después `E01`…`E05`) para que un mismo cuerpo produzca el mismo código. `Accion`, `Canal`, `CodigoEmpresa` y `CodigoModulo` son value objects: no existe una instancia inválida.

**Validación de formato** (`maxLength`, enum `canal`): en el DTO del API con DataAnnotations → `400 E00 "Petición inválida."`. Es responsabilidad del contrato OpenAPI; el dominio no necesita saber que `codigoEmpresa` mide 4.

**Mapeo a HTTP**: solo en el controller. `E01`–`E05` → `200` (incorrecto, pero es lo que hace producción), `E10`/`E00` → `400`, `E99` → `500`.

**Append-only**: el agregado no tiene setters públicos ni métodos de mutación; el repositorio solo expone `Agregar`. El `U`/`D` de `accion` describe la acción auditada, no una operación sobre el registro. Un registro de auditoría no se edita ni se borra; la retención se hace por partición de fecha, no con `DELETE`.

**CQRS**: `RegistrarAuditoriaCommand` pasa por el agregado y el Unit of Work; `ConsultarAuditoriaQuery` lee `AsNoTracking` directo a DTO sin tocar el agregado. Hoy el beneficio es moderado; aparece cuando la reportería sobre millones de filas deje de competir con la escritura fila a fila en Oracle. **Sin MediatR**: dos handlers no justifican una dependencia que además pasó a licencia comercial; `ICommandHandler<,>` e `IQueryHandler<,>` propios en Application, registrados en DI.

**Única desviación de comportamiento**: el legacy responde `200 OK "Procesado"` ante un fallo interno del registro; aquí se responde `500 E99`. `E99` ya existe en el contrato y los consumidores lo manejan en `consulta`. Ocultarlo sería reproducir el defecto #4.

## 3. Qué resuelve el Outbox en este caso

En `BAuditoria.Registrar`, dentro del `TransactionScope`, se hace el `INSERT` y luego `EnviarCorreo`. Dos fallos reales: (a) el correo sale y `ts.Complete()` falla → Seguridad recibe una alerta de una acción que no consta en auditoría; (b) el SMTP tarda 30 s → la transacción de Oracle queda abierta con locks sobre `T01AUDITORIA` mientras los tres canales siguen insertando.

Con el Outbox el command solo escribe en base de datos, y ambas escrituras van en la misma transacción:

```text
UnitOfWork.CommitAsync
    ├── BeginTransaction
    ├── Insertar auditoría
    ├── El agregado confirma y emite AuditoriaRegistrada con su id
    ├── Insertar mensaje en outbox
    └── Commit
```

Si el commit falla, no queda ni registro ni evento; si pasa, el evento existe y se publicará sí o sí. La prueba `Si_falla_el_insert_del_outbox_tampoco_queda_la_auditoria` lo demuestra forzando el fallo con un interceptor. El correo sale del camino transaccional: lo enviará un consumidor del evento.

El `OutboxDispatcher` (`BackgroundService`) toma pendientes con `FOR UPDATE SKIP LOCKED` (varias réplicas sin duplicar), publica, marca `processed_at`; en fallo incrementa `attempts`, guarda `error` y aplica backoff exponencial de 5 a 300 s. El broker caído no afecta al API ni a `/readyz`; se verificó deteniendo RabbitMQ. Semántica **at-least-once**: el consumidor deduplica por `EventId`, que viaja como `MessageId` de AMQP.

## 4. Plan de corte de `/api/auditoria/**`

1. **No-prod primero.** Shadow traffic una semana en QA comparando monolito y servicio: código HTTP, cuerpo y conteo de registros por día. El criterio de aborto se escribe antes de empezar: 5xx > 0,5 % sostenido 5 min, p95 > 300 ms, más de 500 pendientes en el outbox, o diferencia > 0,1 % en el conteo de registros por hora.
2. **Canary con el `HTTPRoute`** de `deploy/k8s/20-httproute-canary.yaml`: 10 % servicio / 90 % monolito durante 24 h; luego 25 → 50 → 100 %, cada paso con un merge en GitOps y las mismas métricas.
3. **Qué se mira**: tasa de error y latencia por backend en el gateway; pendientes y `attempts > 3` en `outbox_messages`; y una comparación de datos: conteo por hora y checksum de `(empresa, usuario, entidad, clave, accion)` entre ambos lados. Un `200` no basta: el monolito guarda hora de Quito y el servicio UTC; si los rangos por fecha divergen, se ve aquí y no en un dashboard de códigos.
4. **Rollback** = PR en GitOps que pone el peso del servicio en `0`. Segundos, sin redeploy, sin tocar el monolito. A las 3 a. m. con registros duplicados: primero peso a 0, después se investiga.
5. **Doble escritura.** Durante el canary ambos escriben en la misma tabla Oracle; los registros se complementan (cada petición la atiende uno). Antes de pasar del 10 % hay que resolver: (a) `SQ01AUDITORIA.NEXTVAL` debe seguir siendo la fuente del `id` en el servicio mientras convivan (EF `UseSequence`); (b) la fecha se normaliza a UTC en el servicio y se convierte al leer, o los consumidores verán saltos de cinco horas.

## 5. Qué queda fuera y qué haría con dos días más

- **Oracle real**: provider `Oracle.EntityFrameworkCore`, mapeo a `T01AUDITORIA`, `UseSequence("SQ01AUDITORIA")`. PostgreSQL es un sustituto.
- **Consumidor del evento** que envíe el correo a Seguridad, con deduplicación por `EventId` y DLQ.
- **Paginación y `fechaHasta`** en un contrato v2 (`/api/v1/auditoria`), conviviendo con v1 en el gateway y con plan de deprecación para móvil, BPM y batch.
- **Retención**: particionado mensual por `fecha_registro` y archivado; hoy solo hay índices.
- **CI/CD**: `dotnet test`, build y escaneo de imagen; Helm chart en lugar de YAML plano; NetworkPolicies y `ServiceMonitor`.
- **OpenTelemetry** (P2 #14): trazas ASP.NET Core y Npgsql, métricas de runtime exportadas por OTLP, y métricas propias `outbox_pendientes` y `outbox_attempts`, que son las que alimentan el criterio de aborto del canary. El `traceId` W3C ya viaja en los logs vía `Activity.Current`, así que se enchufa sin tocar el código de negocio.

## 6. Uso de IA

Trabajé con **Claude Code** como pair programmer, partiendo de una especificación (este documento y el plan de capas) antes del código. Generé con él el esqueleto, las configuraciones EF, el dispatcher, los manifiestos de Kubernetes y la primera versión de las pruebas. Lo que tuve que corregir:

- **Dispatcher vs. reintentos de EF.** Abría una transacción manual con `EnableRetryOnFailure` activo. Compilaba y las pruebas pasaban porque el factory de tests reemplazaba el `DbContext` sin retry; falló en el `docker compose` real. Lo detecté en el smoke test, lo envolví en `CreateExecutionStrategy()` y alineé el factory con la configuración real.
- **`CodigoModulo` como `record struct`.** EF no puede mapear un struct a columna nullable y la migración salió `NOT NULL`. Pasó a `record` de referencia con `Desde()` que devuelve `null`.
- **Proyección LINQ** que accedía a `.Valor` de los value objects dentro del `Select`: no traducible a SQL. Se materializa y se mapea en memoria.
- **Pruebas del outbox** que reseteaban `FakeTimeProvider` hacia atrás (lanza). Se reescribieron relativas al "ahora" del reloj.
- **Helper de tokens** que generaba `nbf` posterior a `exp` en el caso "expirado"; la librería lo rechazaba antes de llegar al API.
- **Organización.** La primera estructura tendía a un archivo por interfaz (varios de 6–13 líneas), un `RegistroAuditoriaResponse` que duplicaba el DTO de Application y OpenTelemetry con cinco paquetes. Consolidé puertos, seguridad y configuraciones EF por tema, eliminé el duplicado y dejé OTel como P2 documentado: menos piezas que explicar y defender, mismo comportamiento, mismas 57 pruebas.
