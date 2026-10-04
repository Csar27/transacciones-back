-- =====================================================================
--  Transacciones - esquema completo, en un solo script.
-- =====================================================================
--  Para que el esquema se pueda aplicar SIN SSDT:
--
--    sqlcmd -S "(local)\SQLEXPRESS" -d TransaccionesDb -E -i deploy.sql
--
--  El proyecto SSDT (Transacciones.Db.sqlproj) es la fuente de verdad para
--  el modelo y los difs; este script es la via practica cuando no hay Visual
--  Studio en la máquina. Si cambian los objetos, hay que actualizar los dos.
--
--  IMPORTANTE: sqlcmd llega con QUOTED_IDENTIFIER en OFF. Sin ponerlo en ON,
--  los indices filtrados y las columnas calculadas fallan al crearse. Por eso
--  las dos primeras lineas del script no son decorativas.
-- =====================================================================

SET QUOTED_IDENTIFIER ON;
GO
SET ANSI_NULLS ON;
GO

-- ---------------------------------------------------------------------
-- Limpieza. Solo para entornos efimeros de desarrollo: en una base real
-- esto no debe ejecutarse nunca.
-- ---------------------------------------------------------------------
IF OBJECT_ID('dbo.trg_Solicitud_Auditoria', 'TR') IS NOT NULL DROP TRIGGER [dbo].[trg_Solicitud_Auditoria];
IF OBJECT_ID('dbo.vw_SolicitudesPendientes', 'V') IS NOT NULL DROP VIEW [dbo].[vw_SolicitudesPendientes];
IF EXISTS (SELECT 1 FROM sys.procedures WHERE name = 'usp_Solicitud_ObtenerPorId') DROP PROCEDURE [dbo].[usp_Solicitud_ObtenerPorId];
IF EXISTS (SELECT 1 FROM sys.procedures WHERE name = 'usp_Solicitud_MarcarVencidas') DROP PROCEDURE [dbo].[usp_Solicitud_MarcarVencidas];
IF EXISTS (SELECT 1 FROM sys.procedures WHERE name = 'usp_Solicitud_GenerarCodigo') DROP PROCEDURE [dbo].[usp_Solicitud_GenerarCodigo];
IF EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'SeqSolicitudCodigo') DROP SEQUENCE [dbo].[SeqSolicitudCodigo];

-- El orden importa: las tablas ANTES que el tipo que usan. Al reves, el motor
-- responde "Cannot drop type ... because it is being referenced".
IF OBJECT_ID('dbo.Solicitud', 'U') IS NOT NULL DROP TABLE [dbo].[Solicitud];
IF OBJECT_ID('dbo.EventoProcesado', 'U') IS NOT NULL DROP TABLE [dbo].[EventoProcesado];
IF OBJECT_ID('dbo.EstadoSolicitud', 'U') IS NOT NULL DROP TABLE [dbo].[EstadoSolicitud];

IF EXISTS (SELECT 1 FROM sys.types WHERE name = 'CodigoEstado') DROP TYPE [dbo].[CodigoEstado];
GO

-- ---------------------------------------------------------------------
-- Tipo definido por el usuario
-- ---------------------------------------------------------------------
CREATE TYPE [dbo].[CodigoEstado] FROM NVARCHAR(20) NOT NULL;
GO

-- ---------------------------------------------------------------------
-- Tablas
-- ---------------------------------------------------------------------
CREATE TABLE [dbo].[EstadoSolicitud]
(
    [Codigo]      [dbo].[CodigoEstado] NOT NULL,
    [Valor]       TINYINT             NOT NULL,
    [Descripcion] NVARCHAR(100)       NOT NULL,
    [EsTerminal]  BIT                 NOT NULL CONSTRAINT [DF_EstadoSolicitud_EsTerminal] DEFAULT (0),
    CONSTRAINT [PK_EstadoSolicitud] PRIMARY KEY CLUSTERED ([Codigo]),
    CONSTRAINT [UQ_EstadoSolicitud_Valor] UNIQUE ([Valor])
);
GO

CREATE TABLE [dbo].[Solicitud]
(
    [Id]               UNIQUEIDENTIFIER NOT NULL,
    [Codigo]           NVARCHAR(30)       NOT NULL,
    [ClienteId]        UNIQUEIDENTIFIER   NOT NULL,
    [Monto]            DECIMAL(18, 2)     NOT NULL,
    [Moneda]           NVARCHAR(3)        NOT NULL CONSTRAINT [DF_Solicitud_Moneda] DEFAULT (N'PEN'),
    [Estado]           TINYINT            NOT NULL CONSTRAINT [DF_Solicitud_Estado] DEFAULT (1),
    [Observacion]      NVARCHAR(500)      NULL,
    [MotivoRechazo]    NVARCHAR(500)      NULL,
    [FechaSolicitud]   DATETIME2(0)       NOT NULL,
    [FechaVencimiento] DATETIME2(0)       NOT NULL,
    [FechaAprobacion]  DATETIME2(0)       NULL,
    [FechaRechazo]     DATETIME2(0)       NULL,
    [FechaCreacion]    DATETIME2(0)       NOT NULL CONSTRAINT [DF_Solicitud_FechaCreacion] DEFAULT (SYSUTCDATETIME()),
    [CreadoPor]        NVARCHAR(100)      NOT NULL CONSTRAINT [DF_Solicitud_CreadoPor] DEFAULT (SUSER_SNAME()),
    [FechaModificacion] DATETIME2(0)      NULL,
    [ModificadoPor]    NVARCHAR(100)      NULL,

    CONSTRAINT [PK_Solicitud] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_Solicitud_Monto]    CHECK ([Monto] > 0),
    CONSTRAINT [CK_Solicitud_Estado]    CHECK ([Estado] BETWEEN 0 AND 5),
    CONSTRAINT [CK_Solicitud_Registro]  CHECK ([FechaVencimiento] > [FechaSolicitud]),
    CONSTRAINT [CK_Solicitud_Rechazo]   CHECK ([Estado] <> 3 OR [MotivoRechazo] IS NOT NULL)
);
GO

CREATE TABLE [dbo].[EventoProcesado]
(
    [EventoId]   UNIQUEIDENTIFIER NOT NULL,
    [Tipo]       NVARCHAR(100)    NOT NULL,
    [RecibidoEn] DATETIME2(0)     NOT NULL CONSTRAINT [DF_EventoProcesado_RecibidoEn] DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_EventoProcesado] PRIMARY KEY CLUSTERED ([EventoId])
);
GO

-- ---------------------------------------------------------------------
-- Indices
-- ---------------------------------------------------------------------
CREATE UNIQUE INDEX [UX_Solicitud_Codigo] ON [dbo].[Solicitud] ([Codigo]);
CREATE INDEX [IX_Solicitud_ClienteId] ON [dbo].[Solicitud] ([ClienteId]);
CREATE INDEX [IX_Solicitud_Estado]    ON [dbo].[Solicitud] ([Estado]);
CREATE INDEX [IX_Solicitud_Fecha]     ON [dbo].[Solicitud] ([FechaSolicitud] DESC);
CREATE INDEX [IX_EventoProcesado_RecibidoEn] ON [dbo].[EventoProcesado] ([RecibidoEn]);

-- Indice compuesto y filtrado: el job de vencimiento solo consulta solicitudes
-- en Borrador o Registrada, asi que el indice cubre exactamente eso.
--
-- El filtro va en forma POSITIVA. El predicado de un indice filtrado no admite
-- NOT en este motor, y ademas listar lo que SI se consulta documenta mejor la
-- intencion que enumerar lo que se descarta.
--
-- Antes de crear un indice filtrado hay que fijar TODAS las opciones SET en el
-- mismo lote. sqlcmd arranca con varias en OFF y el motor responde Msg 1934.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
GO

CREATE INDEX [IX_Solicitud_Vencimiento_NoTerminales]
    ON [dbo].[Solicitud] ([FechaVencimiento], [Estado])
    WHERE [Estado] IN (0, 1);
GO

-- ---------------------------------------------------------------------
-- Correlativo de codigos
-- ---------------------------------------------------------------------
-- SEQUENCE y no "MAX(Codigo) + 1": dos peticiones concurrentes leerian el
-- mismo maximo y generarian el mismo codigo. La SEQUENCE es atomica.
CREATE SEQUENCE [dbo].[SeqSolicitudCodigo]
    AS BIGINT
    START WITH 1
    INCREMENT BY 1
    MINVALUE 1
    NO MAXVALUE
    CACHE 20;
GO

CREATE PROCEDURE [dbo].[usp_Solicitud_GenerarCodigo]
    @Anio SMALLINT
AS
BEGIN
    SET NOCOUNT ON;

    -- Devuelve una FILA, no un parametro OUTPUT: con Dapper, un OUTPUT obliga a
    -- declararlo aparte con DynamicParameters. Como fila se mapea directo.
    SELECT CAST(
        CONCAT(
            'SOL-',
            FORMAT(@Anio, '0000'),
            '-',
            FORMAT(NEXT VALUE FOR [dbo].[SeqSolicitudCodigo], '000000'))
        AS NVARCHAR(30)) AS Codigo;
END
GO

-- ---------------------------------------------------------------------
-- Vistas y procedimientos
-- ---------------------------------------------------------------------
CREATE VIEW [dbo].[vw_SolicitudesPendientes]
AS
    SELECT [Codigo], [ClienteId], [Monto], [Moneda], [Estado], [FechaVencimiento]
    FROM [dbo].[Solicitud]
    WHERE [Estado] IN (0, 1, 2);
GO

CREATE PROCEDURE [dbo].[usp_Solicitud_ObtenerPorId]
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT [Id], [Codigo], [ClienteId], [Monto], [Moneda], [Estado], [Observacion],
           [MotivoRechazo], [FechaSolicitud], [FechaVencimiento], [FechaAprobacion],
           [FechaRechazo], [FechaCreacion], [CreadoPor]
    FROM [dbo].[Solicitud]
    WHERE [Id] = @Id;
END
GO

CREATE PROCEDURE [dbo].[usp_Solicitud_MarcarVencidas]
    @FechaCorte DATETIME2(0),
    @FilasAfectadas INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE [dbo].[Solicitud]
       SET [Estado] = 4, [FechaModificacion] = @FechaCorte
     WHERE [Estado] IN (0, 1) AND [FechaVencimiento] < @FechaCorte;

    SET @FilasAfectadas = @@ROWCOUNT;
END
GO

-- ---------------------------------------------------------------------
-- Disparadores
-- ---------------------------------------------------------------------
-- NO hay ningun trigger sobre dbo.Solicitud, y es deliberado.
--
-- Hubo uno, trg_Solicitud_Auditoria, que estampaba FechaModificacion y
-- ModificadoPor con un UPDATE sobre la MISMA tabla que lo habia disparado.
--
-- Ese constructo solo funciona mientras RECURSIVE_TRIGGERS este apagado. En
-- cuanto alguien lo activa en la base, el trigger se vuelve a disparar sobre si
-- mismo y agota el limite de anidamiento. El fallo sale unicamente al ACTUALIZAR
-- y nunca al insertar: por eso crear solicitudes funcionaba, aprobar no, y al
-- cliente le llegaba un 500 sin detalle.
--
-- La auditoria la estampa ahora la aplicacion, en SolicitudService, con el
-- mismo criterio que CreadoPor. Se puede comprobar con:
--
--   SELECT Codigo, Estado, FechaModificacion, ModificadoPor FROM dbo.Solicitud;
--
-- Si al desplegar esto contra una base que ya tenga el trigger, el DROP de abajo
-- lo retira; si no, no tiene efecto.
DROP TRIGGER IF EXISTS [dbo].[trg_Solicitud_Auditoria];
GO

-- ---------------------------------------------------------------------
-- Datos iniciales
-- ---------------------------------------------------------------------
MERGE [dbo].[EstadoSolicitud] AS destino
USING (VALUES
    (N'Borrador',   0, N'Solicitud creada, aun no enviada',     0),
    (N'Registrada', 1, N'Registrada y pendiente de validacion',  0),
    (N'Aprobada',   2, N'Aprobada por el flujo correspondiente', 0),
    (N'Rechazada',  3, N'Rechazada. Estado terminal',            1),
    (N'Vencida',    4, N'No atendida antes del vencimiento',    1),
    (N'Anulada',    5, N'Anulada por el usuario. Estado terminal', 1)
) AS origen ([Codigo], [Valor], [Descripcion], [EsTerminal])
    ON destino.[Codigo] = origen.[Codigo]
WHEN MATCHED THEN
    UPDATE SET [Valor] = origen.[Valor], [Descripcion] = origen.[Descripcion], [EsTerminal] = origen.[EsTerminal]
WHEN NOT MATCHED THEN
    INSERT ([Codigo], [Valor], [Descripcion], [EsTerminal])
    VALUES (origen.[Codigo], origen.[Valor], origen.[Descripcion], origen.[EsTerminal]);
GO

PRINT 'Esquema de Transacciones aplicado.';
GO

-- ---------------------------------------------------------------------
-- Alinear la secuencia con los datos que ya hubiera.
--
-- Sin esto, sobre una base que ya tiene solicitudes, la secuencia arrancaria en
-- 1 y el primer POST chocaria contra el indice unico con un 409. Hay que
-- dejarla por encima del maximo existente.
--
-- Usa RIGHT(Codigo, 6) y no REPLACE(Codigo, 'SOL-', ''): el codigo incluye el
-- ano, asi que quitar solo el prefijo deja '2026-000001', que NO es convertible
-- a BIGINT. TRY_CONVERT devuelve NULL, MAX devuelve NULL, y el correlativo se
-- queda siempre en 1. Es el bug que hacia fallar el POST.
-- ---------------------------------------------------------------------
DECLARE @maximo BIGINT = (
    SELECT ISNULL(MAX(TRY_CONVERT(BIGINT, RIGHT([Codigo], 6))), 0)
    FROM [dbo].[Solicitud]
    WHERE [Codigo] LIKE 'SOL-%'
);

DECLARE @actual BIGINT = NEXT VALUE FOR [dbo].[SeqSolicitudCodigo];

IF @actual <= @maximo
BEGIN
    -- ALTER SEQUENCE exige un LITERAL, no una variable. Hay que pasar por SQL
    -- dinamico. Concatenar un numero ya convertido a texto es seguro (no es
    -- entrada de usuario), pero conviene decirlo.
    DECLARE @sql NVARCHAR(200) =
        N'ALTER SEQUENCE [dbo].[SeqSolicitudCodigo] RESTART WITH '
        + CAST(@maximo + 1 AS NVARCHAR(20));

    EXEC sp_executesql @sql;

    PRINT 'Secuencia resincronizada a ' + CAST(@maximo + 1 AS VARCHAR(20));
END
GO