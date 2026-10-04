# Transacciones

Gestión de solicitudes de transacciones: una API .NET y su interfaz web, en dos
carpetas hermanas con una responsabilidad cada una.

```
Transacciones\
├── backend\     API .NET 8 por capas — .NET here
└── frontend\    Interfaz React + Vite — JavaScript here
```

Cada carpeta tiene su propio README, sus propias dependencias y su propio
ciclo de compilación. Se despliegan por separado: la API no sabe que el
frontend existe, y el frontend no compila nada de .NET.

---

## Arrancar los dos

Dos terminales. El orden importa solo para ver datos: sin solicitudes en la base,
la interfaz arranca igual y sale la pantalla de "todavía no hay solicitudes".

### 1. API

```powershell
cd D:\projects\Transacciones\backend
dotnet build Transacciones.sln -c Release
dotnet run --project Transacciones.WebApi\Transacciones.WebApi.csproj -c Release
```

Escucha en **http://localhost:5250**. Swagger en
**http://localhost:5250/swagger**.

La base de datos tiene que existir. El esquema está en
`backend\Transacciones.Db\deploy.sql`:

```powershell
sqlcmd -S "(local)\SQLEXPRESS" -d TransaccionesDb -E -b -i .\Transacciones.Db\deploy.sql
```

> Ese script **borra las tablas** antes de crearlas. Es para desarrollo.

### 2. Interfaz

```powershell
cd D:\projects\Transacciones\frontend
npm install
npm run dev
```

Abre **http://localhost:5173**.

El 5173 es porque el proxy de Vite está en medio: la API solo acepta CORS desde
`localhost:4200` y `localhost:5173`, y el navegador nunca ve la petición a la
API — la reenvía el servidor de Vite. Por eso `VITE_API_URL` es `/api` y no la
URL de la API.

---

## Qué hay en cada lado

| | `backend/` | `frontend/` |
|---|---|---|
| Tecnología | .NET 8 | JavaScript · React 19 · Vite |
| Responsabilidad | Reglas de negocio, datos, contrato | Presentación, estado de cliente |
| Pruebas | 81 con xUnit | 4 suites con Vitest |
| Documentación | [`backend/README.md`](backend/README.md) | [`frontend/README.md`](frontend/README.md) |

La línea está donde tiene que estar: **las reglas de negocio viven en el
backend**. El frontend no decide si una solicitud se puede aprobar — lo pregunta,
y si la API responde 409, lo muestra.

---

## Comprobar que todo está en su sitio

```powershell
cd D:\projects\Transacciones\backend
.\validar-capas.ps1     # falla con codigo 1 si alguien rompe el reparto de capas

cd D:\projects\Transacciones\frontend
npm run lint
npm test
npm run build
```

---

## Las dos cabeceras que la API lee

| Cabecera | Efecto |
|---|---|
| `X_isMultiRisk: true` | Enruta la petición a la base SME en vez de a la normal |
| `X_userId` | Escribe ese valor en `creadoPor` |

El frontend **no envía ninguna de las dos**. `creadoPor` sale como `"sistema"` y
todas las peticiones van a la base normal. El interceptor de Axios ya está
preparado para añadir un `Authorization: Bearer` cuando exista autenticación:
basta con registrar el proveedor del token.

---

## Migración reciente

El backend se movió de la raíz a `backend\` con `mover-backend.ps1`. El script
limpia `bin\`, `obj\` y `.vs\` antes de mover, porque esos artefactos guardan
rutas absolutas a la ubicación antigua.