-- Registro de mensajes ya procesados.
--
-- Es la base del patron Inbox: la clave primaria sobre EventoId es lo que
-- deduplica. Un consumidor inserta aqui antes de hacer su trabajo; si el
-- INSERT falla por violacion de clave, el mensaje ya se proceso antes y lo
-- descarta en silencio.
CREATE TABLE [dbo].[EventoProcesado]
(
    [EventoId]   UNIQUEIDENTIFIER NOT NULL,
    [Tipo]       NVARCHAR(100)    NOT NULL,
    [RecibidoEn] DATETIME2(0)     NOT NULL
        CONSTRAINT [DF_EventoProcesado_RecibidoEn] DEFAULT (SYSUTCDATETIME()),

    CONSTRAINT [PK_EventoProcesado] PRIMARY KEY CLUSTERED ([EventoId])
);

-- La limpieza.programada necesita buscar por fecha: sin este indice, el DELETE
-- de purga recorre toda la tabla.
CREATE INDEX [IX_EventoProcesado_RecibidoEn] ON [dbo].[EventoProcesado] ([RecibidoEn]);