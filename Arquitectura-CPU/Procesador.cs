﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Arquitectura_CPU
{
    class Procesador
    {
        #region constantes
        // constantes: para salud mental

        public const int CACHDAT_FILAS = 4;
        public const int CACHDAT_COLUMNAS = 5;
        public const int CACHDAT_COL_ESTADO = 4;

        public const int ESTADO_INVALIDO = 0;
        public const int ESTADO_COMPARTIDO = 1;
        public const int ESTADO_MODIFICADO = 2;

        public const int DIRECT_FILAS = 8;
        public const int DIRECT_COLUMNAS = 4;
        public const int DIRECT_COL_ESTADO = 0;

        public const int ESTADO_UNCACHED = 0;
        public const int BLOQUES_COMP = 8;
        #endregion
        
        // estructuras de datos del procesador
        public int[][][] cacheInstrucciones;
        public int[][][] memoriaPrincipal;
        public int[] blockMap = new int[4];

        private bool estoyEnRetraso;
        private int ciclosEnRetraso;

        // referente a sincronizacion
        public Barrier sync;
        public int id, cicloActual, maxCiclo;
        public int quantum;
        public List<Contexto> contextos, contextosFinalizados;

        // directorio 
        // 8 columnas -> cantidad de bloques memo compart
        // 4 columnas -> 0 estado, 3 cada procesador
        public int[][] directorio;

        // cacheDatos[4][5]
        // 4 filas -> cantidad de bloques de la cache
        // 5 columnas -> 0 para ESTADO, 4 de DATOS
        public int[][] cacheDatos;
        public int[] blockMapDatos;

        private Consola console;

        public List<Procesador> procesadores { get; set; }
        

        public Procesador(int id, int maxCiclo, Barrier s, List<string> programas, Consola c, int recievedQuantum)
        {
            console = c;
            this.sync = s;
            this.id = id;
            cicloActual = 1;
            this.maxCiclo = maxCiclo;
            estoyEnRetraso = false;
            ciclosEnRetraso = 0;

            quantum = recievedQuantum;

            cacheInstrucciones = new int[4][][];
            for (int i = 0; i < 4; i++)
            {
                blockMap[i] = -1;
                cacheInstrucciones[i] = new int[4][];
                for (int j = 0; j < 4; j++)
                {
                    cacheInstrucciones[i][j] = new int[4];
                    for (int k = 0; k < 4; k++)
                    {
                        cacheInstrucciones[i][j][k] = 0;
                    }
                }
            }

            directorio = new int[DIRECT_FILAS][];
            for (int j = 0; j < DIRECT_FILAS; j++)
            {
                directorio[j] = new int[DIRECT_COLUMNAS];
                for (int k = 0; k < DIRECT_COLUMNAS; k++)
                {
                    if (k == DIRECT_COL_ESTADO)
                    {
                        directorio[j][k] = ESTADO_UNCACHED; 
                    }
                    else 
                    {
                        directorio[j][k] = -1;
                    }
                }
            }

            cacheDatos = new int[CACHDAT_FILAS][];
            blockMapDatos = new int[CACHDAT_FILAS];

            for (int j = 0; j < CACHDAT_FILAS; j++)
            {
                blockMapDatos[j] = -1;
                cacheDatos[j] = new int[CACHDAT_COLUMNAS];
                for (int k = 0; k < CACHDAT_COLUMNAS; k++)
                {  
                    if (k == CACHDAT_COL_ESTADO)
                    {
                        cacheDatos[j][k] = ESTADO_INVALIDO;
                    }
                    else
                    {
                        cacheDatos[j][k] = -1;
                    }
                }
            }

            memoriaPrincipal = new int[24][][];
            //parte compartida
            for (int i = 0; i < 8; i++)
            {
                memoriaPrincipal[i] = new int[4][];
                for (int j = 0; j < 4; j++)
                {
                    memoriaPrincipal[i][j] = new int[4];
                    for(int k = 0; k < 4; k++)
                    {
                        memoriaPrincipal[i][j][k] = 1;
                    }
                    
                }
            }
            //parte no compartida 
            for (int i = 8; i < 24; i++)
            {
                memoriaPrincipal[i] = new int[4][];
                for (int j = 0; j < 4; j++)
                {
                    memoriaPrincipal[i][j] = new int[4];
                    for (int k = 0; k < 4; k++)
                    {
                        memoriaPrincipal[i][j][k] = 0;
                    }
                }
            }

            contextos = new List<Contexto>();
            contextosFinalizados = new List<Contexto>();
            manejoArchivo(programas);

        }

        /// <summary>
        /// Recibe las referencias de los procesadores
        /// </summary>
        /// <param name="p"></param>
        public void setProcesadores(List<Procesador> p)
        {
            procesadores = p;
        }

        public static void ShiftLeft<T>(List<T> lst, int shifts)
        {
            for (int i = 0; i < shifts; i++)
            {
                lst.Add(lst.ElementAt(0));
                lst.RemoveAt(0);
            }
        }

        public void manejoArchivo(List<string> programas)
        {
            int direccionRam = 128;
            int idPrograma = 1;
            foreach (var p in programas)
            {
                // para cada programa

                // cada linea es una instruccion de 4 numeros
                string[] instrucciones = p.Split('\n');

                Contexto contexto = new Contexto(direccionRam, idPrograma, id);
                contextos.Add(contexto);

                foreach (var i in instrucciones)
                {
                    // para cada instruccion separo los 4 numeros
                    string[] instruccion = i.Split(' ');
                    int[] numeros = Array.ConvertAll(instruccion, int.Parse);

                    for (int m = 0; m < 4; m++)
                    {
                        var direccion = getPosicion(direccionRam);
                        memoriaPrincipal[direccion.Item1][direccion.Item2][m] = numeros[m];
                    }
                    direccionRam += 4;
                }
                idPrograma++;
            }
            contextos.ElementAt(0).cicloInicial = 1;
        }

        public string getStringInstruccion(int[] instruccion)
        {
            int codigoInstruccion = instruccion[0],
                regFuente1 = instruccion[1],
                regFuente2 = instruccion[2],
                regDest = instruccion[3];
            string res = "abc";
            Contexto contPrincipal = contextos.ElementAt(0);

            switch (codigoInstruccion)
            {
                case 8:
                    /*
                    DADDI RX, RY, #n : Rx <-- (Ry) + n
                    CodOp: 8 RF1: Y RF2 O RD: x RD O IMM:n
                    */
                    res = String.Format("DADDI R{0},{1},{2}", regFuente1, contPrincipal.registro[regFuente2], regDest);
                    break;
                case 32:
                    /*
                    DADD RX, RY, #n : Rx <-- (Ry) + (Rz)
                    CodOp: 32 RF1: Y RF2 O RD: x RD o IMM:Rz
                    */
                    res = String.Format("DADD R{0},{1},{2}", regFuente1, contPrincipal.registro[regFuente2], contPrincipal.registro[regDest]);
                    break;
                case 34:
                    /*
                    DSUB RX, RY, #n : Rx <-- (Ry) - (Rz)
                    CodOp: 34 RF1: Y RF2 O RD: z RD o IMM:X
                    */
                    res = String.Format("DSUB R{0},{1},{2}", regFuente1, contPrincipal.registro[regFuente2], contPrincipal.registro[regDest]);
                    break;
                case 12:
                    /*
                    DMUL RX, RY, #n : Rx <-- (Ry) * (Rz)
                    CodOp: 12 RF1: Y RF2 O RD: z RD o IMM:X
                    */
                    res = String.Format("DMUL R{0},{1},{2}", regFuente1, contPrincipal.registro[regFuente2], contPrincipal.registro[regDest]);
                    break;
                case 14:
                    /*
                    DDIV RX, RY, #n : Rx <-- (Ry) / (Rz)
                    CodOp: 14 RF1: Y RF2 O RD: z RD o IMM:X
                    */
                    res = String.Format("DDIV R{0},{1},{2}", regFuente1, contPrincipal.registro[regFuente2], contPrincipal.registro[regDest]);
                    break;
                case 4:
                    /*
                    BEQZ RX, ETIQ : Si RX = 0 salta 
                    CodOp: 4 RF1: Y RF2 O RD: 0 RD o IMM:n
                    */
                    res = String.Format("BEQZ R{0},{1}", contPrincipal.registro[regFuente1], regDest);
                    break;
                case 5:
                    /*
                     BEQNZ RX, ETIQ : Si RX != 0 salta 
                     CodOp: 5 RF1: x RF2 O RD: 0 RD o IMM:n
                     */
                    res = String.Format("BEQNZ R{0},{1}", contPrincipal.registro[regFuente1], regDest);
                    break;
                case 3:
                    /*
                    JAL n, R31=PC, PC = PC+n
                    CodOp: 3 RF1: 0 RF2 O RD: 0 RD o IMM:n
                    */
                    res = String.Format("JAL {0}", regDest);
                    break;
                case 2:
                    /*
                    JR RX: PC=RX
                    CodOp: 2 RF1: X RF2 O RD: 0 RD o IMM:0
                    */
                    res = String.Format("JR RX{0}", contPrincipal.registro[regFuente1]);
                    break;
                case 63:
                    /*
                     fin
                     CodOp: 63 RF1: 0 RF2 O RD: 0 RD o IMM:0
                     */
                    res = String.Format("FIN");
                    break;
                case 35:
                    /* *
                    * LW Rx, n(Ry)
                    * Rx <- M(n + (Ry))
                    * 
                    * codOp: 35 RF1: Y RF2 O RD: X RD O IMM: n
                    * */
                    res = String.Format("LW R{0} {1}(R{2})", regFuente2, regDest, regFuente1);
                    break;
                case 50:
                    /* *
                     * LL Rx, n(Ry)
                     * Rx <- M(n + (Ry))
                     * Rl <- n+(Ry)
                     * codOp: 50 RF1: Y RF2 O RD: X RD O IMM: n
                     * */
                    res = String.Format("LL R{0} {1}(R{2})", regFuente2, regDest, regFuente1);
                    break;
                case 51:
                    /* *
                     * SC RX, n(rY)
                     * IF (rl = N+(Ry)) => m(N+(RY)) = rX
                     * ELSE Rx =0
                     *  codOp: 51 RF1: Y RF2 O RD: X RD O IMM: n
                     * */
                    res = String.Format("SC R{0} {1}(R{2})", regFuente2, regDest, regFuente1);
                    break;
                case 43:
                    /* *
                     * SW RX, n(rY)
                     * m(N+(RY)) = rX
                     * codOp: 51 RF1: Y RF2 O RD: X RD O IMM: n
                     * */
                    res = String.Format("SW R{0} {1}(R{2})", regFuente2, regDest, regFuente1);
                    break;
            }
            return res;
        }

        public bool manejoInstrucciones(int[] instruccion)
        {
            bool res = false;
            int codigoInstruccion = instruccion[0],
                regFuente1 = instruccion[1],
                regFuente2 = instruccion[2],
                regDest = instruccion[3],
                posMem = 0;

            Contexto contPrincipal = contextos.ElementAt(0);

            contPrincipal.pc += 4;
            switch (codigoInstruccion)
            {
                case 8:
                    /*
                    DADDI RX, RY, #n : Rx <-- (Ry) + n
                    CodOp: 8 RF1: Y RF2 O RD: x RD O IMM:n
                    */
                    contPrincipal.registro[regFuente2] = contPrincipal.registro[regFuente1] + regDest;
                    break;
                case 32:
                    /*
                    DADD RX, RY, #n : Rx <-- (Ry) + (Rz)
                    CodOp: 32 RF1: Y RF2 O RD: x RD o IMM:Rz
                    */
                    contPrincipal.registro[regDest] = contPrincipal.registro[regFuente1] + contPrincipal.registro[regFuente2];
                    break;
                case 34:
                    /*
                    DSUB RX, RY, #n : Rx <-- (Ry) - (Rz)
                    CodOp: 34 RF1: Y RF2 O RD: z RD o IMM:X
                    */
                    contPrincipal.registro[regDest] = contPrincipal.registro[regFuente1] - contPrincipal.registro[regFuente2];
                    break;
                case 12:
                    /*
                    DMUL RX, RY, #n : Rx <-- (Ry) * (Rz)
                    CodOp: 12 RF1: Y RF2 O RD: z RD o IMM:X
                    */
                    contPrincipal.registro[regDest] = contPrincipal.registro[regFuente1] * contPrincipal.registro[regFuente2];
                    break;
                case 14:
                    /*
                    DDIV RX, RY, #n : Rx <-- (Ry) / (Rz)
                    CodOp: 14 RF1: Y RF2 O RD: z RD o IMM:X
                    */
                    contPrincipal.registro[regDest] = contPrincipal.registro[regFuente1] / contPrincipal.registro[regFuente2];
                    break;
                case 4:
                    /*
                    BEQZ RX, ETIQ : Si RX = 0 salta 
                    CodOp: 4 RF1: Y RF2 O RD: 0 RD o IMM:n
                    */
                    if (contPrincipal.registro[regFuente1] == 0)
                    {
                        contPrincipal.pc += (regDest << 2);
                        //salta a la etiqueta indicada por regDest
                    }
                    break;
                case 5:
                    /*
                     BEQNZ RX, ETIQ : Si RX != 0 salta 
                     CodOp: 5 RF1: x RF2 O RD: 0 RD o IMM:n
                     */
                    if (contPrincipal.registro[regFuente1] != 0)
                    {
                        //salta a la etiqueta indicada por regDest
                        contPrincipal.pc += (regDest << 2);
                    }
                    break;
                case 3:
                    /*
                    JAL n, R31=PC, PC = PC+n
                    CodOp: 3 RF1: 0 RF2 O RD: 0 RD o IMM:n
                    */
                    contPrincipal.registro[31] = contPrincipal.pc;
                    contPrincipal.pc += regDest;
                    break;
                case 2:
                    /*
                    JR RX: PC=RX
                    CodOp: 2 RF1: X RF2 O RD: 0 RD o IMM:0
                    */
                    contPrincipal.pc = contPrincipal.registro[regFuente1];
                    break;
                case 63:
                    /*
                     fin
                     CodOp: 63 RF1: 0 RF2 O RD: 0 RD o IMM:0
                     */
                    res = true;
                    break;
                case 50:
                    /* *
                     * LL Rx, n(Ry)
                     * Rx <- M(n + (Ry))
                     * Rl <- n+(Ry)
                     * codOp: 50 RF1: Y RF2 O RD: X RD O IMM: n
                     * */
                    posMem = contPrincipal.registro[regFuente1] + regDest;
                    loadLink(regFuente2, posMem);
                    break;
                case 51:
                    /* *
                     * SC RX, n(rY)
                     * IF (rl = N+(Ry)) => m(N+(RY)) = rX
                     * ELSE Rx =0
                     *  codOp: 51 RF1: Y RF2 O RD: X RD O IMM: n
                     * */
                    posMem = contPrincipal.registro[regFuente1] + regDest;
                    storeConditional(posMem, regFuente2);
                    break;
                case 35:
                    /* *
                    * LW Rx, n(Ry)
                    * Rx <- M(n + (Ry))
                    * 
                    * codOp: 35 RF1: Y RF2 O RD: X RD O IMM: n
                    * */
                    posMem = contPrincipal.registro[regFuente1] + regDest;
                    loadWord(regFuente2, posMem);
                    break;
                case 43:
                    /* *
                     * SW RX, n(rY)
                     * m(N+(RY)) = rX
                     * codOp: 51 RF1: Y RF2 O RD: X RD O IMM: n
                     * */
                    posMem = contPrincipal.registro[regFuente1] + regDest;
                    storeWord(posMem, regFuente2);
                    break;
            }
            return res;
        }

        public bool bloqueEnMiCache(Tuple<int, int> direccion)
        {
            return blockMapDatos[direccion.Item1 % CACHDAT_FILAS] == direccion.Item1;
        }

        public bool invalidarEnOtrasCaches(Tuple<int, int> direccion, int numProc, int valRegFuente, bool hit)
        {
            int i = 0;
            bool bloqueoTodasLasCaches = true;
            for (i = 0; i < 3 && bloqueoTodasLasCaches; i++) // valido la bandera aca de una vez
            {
                if(i != id) // solo invalida trata de bloquear las otras caches
                {
                    if (procesadores.ElementAt(numProc).directorio[direccion.Item1 % DIRECT_FILAS][i + 1] == ESTADO_COMPARTIDO) // si está en uno es xq esa cache lo tiene C
                    {
                        bool bloqueoCacheActual = false;
                        try
                        {
                            Monitor.TryEnter(procesadores.ElementAt(i).cacheDatos, ref bloqueoCacheActual);
                            if (bloqueoCacheActual)
                            {
                                procesadores.ElementAt(numProc).directorio[direccion.Item1 % DIRECT_FILAS][i + 1] = ESTADO_INVALIDO; // invalida en el directorio 
                                procesadores.ElementAt(i).cacheDatos[direccion.Item1 % CACHDAT_FILAS][CACHDAT_COL_ESTADO] = ESTADO_INVALIDO; // invalida en la caché
                                procesadores.ElementAt(i).blockMap[direccion.Item1 % CACHDAT_FILAS] = -1;
                            }
                            else
                            {
                                bloqueoTodasLasCaches = false;
                            }
                        }
                        finally
                        {
                            if (bloqueoCacheActual)
                            {
                                Monitor.Exit(procesadores.ElementAt(i).cacheDatos);
                            }
                        }
                    }
                }
            }
 
            if (bloqueoTodasLasCaches)
            {
                procesadores.ElementAt(numProc).directorio[(direccion.Item1) % DIRECT_FILAS][DIRECT_COL_ESTADO] = ESTADO_MODIFICADO; // pone estado en Modificado en el directorio
                procesadores.ElementAt(numProc).directorio[(direccion.Item1) % DIRECT_FILAS][id + 1] = 1; // indica que en el procesador numero id tiene al bloque modificado 
                if (hit)
                {
                    this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][CACHDAT_COL_ESTADO] = ESTADO_INVALIDO; // modifica el estado del bloque en la cache
                    this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][direccion.Item2] = valRegFuente;
                }
                else
                {
                    jalarBloqueDeMemoria(direccion, numProc, valRegFuente);
                }
            }
            return bloqueoTodasLasCaches;
        }

        public void jalarBloqueDeMemoria(Tuple<int, int> direccion, int numProcBloque, int valRegFuente)
        {
            int bloke = direccion.Item1 % CACHDAT_FILAS;
            this.cacheDatos[bloke][CACHDAT_COL_ESTADO] = ESTADO_MODIFICADO; // lo pone en la cache como modificado  
            this.blockMapDatos[bloke] = direccion.Item1; // pone la etiqueta del bloque 

            for (int i = 0; i < 4; i++)
            {
                this.cacheDatos[bloke][i] = procesadores.ElementAt(numProcBloque).memoriaPrincipal[direccion.Item1 % BLOQUES_COMP][i][0];
            }

            this.cacheDatos[bloke][direccion.Item2] = valRegFuente;

            procesadores.ElementAt(numProcBloque).directorio[direccion.Item1 % DIRECT_FILAS][DIRECT_COL_ESTADO] = ESTADO_MODIFICADO; // lo pone modificado en el directorio 
            procesadores.ElementAt(numProcBloque).directorio[direccion.Item1 % DIRECT_FILAS][id + 1] = 1; // indica la cache del procesador en el que esta modificado 
        }
        
        public void storeWord(int posMem, int regFuente)
        {
            bool bloqueoMiCache = false;
            bool bloqueoDirecCasa = false;
            bool bloqueoDirecVictima = false;
            bool bloqueoDirecBloque = false;

            // en caso que no se logre lock de directorio bloque victima no deja pasar
            bool puedeContinuarDesdeBloqueVictima = true;

            object objMiCache = null;
            object objDirecCasa = null;
            object objDirecVictima = null;
            object objDirecBloque = null;
            try
            {
                Contexto contPrincipal = contextos.ElementAt(0);

                Monitor.TryEnter(this.cacheDatos, ref bloqueoMiCache);
                if(bloqueoMiCache)
                {
                    #region bloqueoMiCache
                    objMiCache = this.cacheDatos;
                    var direccion = getPosicion(posMem);

                    if (bloqueEnMiCache(direccion))
                    {
                        #region hitMiCache
                        int estadoMiBloque = this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][CACHDAT_COL_ESTADO];
                        // pregunta por el estado
                        switch (estadoMiBloque)
                        {
                            case ESTADO_COMPARTIDO:
                                #region Compartido
                                // el bloque esta C 
                                // pide directorio casa del bloque que esta C

                                int numProc = getNumeroProcesador(direccion.Item1);
                                Monitor.TryEnter(procesadores.ElementAt(numProc).directorio, ref bloqueoDirecCasa);
                                if (bloqueoDirecCasa)
                                {
                                    objDirecCasa = procesadores.ElementAt(numProc).directorio;
                                    // busco cuales otros lo tienen en C 
                                    // si hay, bloqueo caches e invalido
                                    // me devuelvo a lo mio y modifico
                                    // actualizo el directorio
                                    bool res = invalidarEnOtrasCaches(direccion, numProc, contPrincipal.registro[regFuente], true);
                                    if(!res)
                                    {
                                        //libero 
                                        contPrincipal.pc -= 4;
                                    }
                                }
                                else
                                {
                                    //libero 
                                    contPrincipal.pc -= 4;
                                }
                                break;
                            #endregion
                            case ESTADO_MODIFICADO:
                                // si el bloque esta M 
                                this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][direccion.Item2] = contPrincipal.registro[regFuente]; // no estoy segura que esa sea la pos de mem
                                break;
                        }
                        #endregion
                    }
                    else
                    {
                        #region missMiCache
                        int estadoBloqueVictima = this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][CACHDAT_COL_ESTADO];

                        if (estadoBloqueVictima == ESTADO_COMPARTIDO || estadoBloqueVictima == ESTADO_MODIFICADO)
                        {
                            // pide directorio de bloque victima 
                            // lo bloquea 
                            int numeroBloqueVictima = this.blockMapDatos[direccion.Item1 % CACHDAT_FILAS];
                            int procesadorBloqueVictima = getNumeroProcesador(numeroBloqueVictima);

                            Monitor.TryEnter(procesadores.ElementAt(procesadorBloqueVictima).directorio, ref bloqueoDirecVictima);
                            if (bloqueoDirecVictima)
                            {
                                #region bloqueoDirecVictima
                                objDirecVictima = procesadores.ElementAt(procesadorBloqueVictima).directorio;
                                if (estadoBloqueVictima == ESTADO_COMPARTIDO)
                                {
                                    procesadores.ElementAt(procesadorBloqueVictima).directorio[direccion.Item1 % DIRECT_FILAS][procesadorBloqueVictima] = ESTADO_UNCACHED;  // actualiza el directorio poniendo cero
                                    this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][CACHDAT_COL_ESTADO] = ESTADO_INVALIDO;// poner I cache propia
                                }
                                else if (estadoBloqueVictima == ESTADO_MODIFICADO)
                                {
                                    // manda a guardar el bloque   
                                    for (int i = 0; i < 4; i++)
                                    {
                                        procesadores.ElementAt(procesadorBloqueVictima).memoriaPrincipal[numeroBloqueVictima % BLOQUES_COMP][i][0] = this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][i];
                                    }
                                    procesadores.ElementAt(procesadorBloqueVictima).directorio[direccion.Item1 % CACHDAT_FILAS][procesadorBloqueVictima] = ESTADO_UNCACHED;  // actualiza el directorio poniendo cero
                                    this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][DIRECT_COL_ESTADO] = ESTADO_INVALIDO;// poner I cache propia
                                }
                                #endregion
                            }
                            else
                            {
                                contPrincipal.pc -= 4;
                                puedeContinuarDesdeBloqueVictima = false;
                            }
                        }
                        if(puedeContinuarDesdeBloqueVictima)
                        {
                            int numProcBloque = getNumeroProcesador(direccion.Item1);
                            Monitor.TryEnter(procesadores.ElementAt(numProcBloque).directorio, ref bloqueoDirecBloque);
                            if (bloqueoDirecBloque)
                            {
                                #region bloqueoDirecBloque
                                objDirecBloque = procesadores.ElementAt(numProcBloque).directorio;
                                // tengo directorio bloque que quiero leer
                                int estadoBloque = procesadores.ElementAt(numProcBloque).directorio[direccion.Item1 % DIRECT_FILAS][DIRECT_COL_ESTADO];
                                // estados son U C M
                                switch (estadoBloque)
                                {
                                    case ESTADO_UNCACHED:
                                        // U
                                        // lo jala de memoria, lo guarda en mi cache y modifica el directorio
                                        // actualiza
                                        jalarBloqueDeMemoria(direccion, numProcBloque, contPrincipal.registro[regFuente]);
                                        break;
                                    case ESTADO_COMPARTIDO:
                                        // C
                                        // fijarse en directorio, invalidar todo si alguien lo tiene
                                        // lo jala de memoria, lo guarda en mi cache y modifica el directorio
                                        bool resC = invalidarEnOtrasCaches(direccion, numProcBloque, contPrincipal.registro[regFuente], false);
                                        if(!resC)
                                        {
                                            contPrincipal.pc -= 4;
                                        }
                                        //el metodo invalidarEnOtrasCaches llama a jalarBloqueDeMemoria q se encarga de jalar el bloque, modificar la cache y directorio
                                        break;
                                    case ESTADO_MODIFICADO:
                                        // M
                                        // bloquea cache de donde esta
                                        // guarda en memoria, actualiza directorio
                                        // jala el bloque a mi cache y lo modifica
                                        // invalidar en la otra caché
                                        bool resM = invalidarEnOtrasCaches(direccion, numProcBloque, contPrincipal.registro[regFuente], false);
                                        if (!resM)
                                        {
                                            contPrincipal.pc -= 4;
                                        }
                                        //el metodo invalidarEnOtrasCaches llama a jalarBloqueDeMemoria q se encarga de jalar el bloque, modificar la cache y directorio
                                        break;
                                }
                                #endregion
                            }
                            else
                            {
                                contPrincipal.pc -= 4;
                            }
                        }
                        #endregion
                    }
                    #endregion
                }
                else
                {
                    contPrincipal.pc -= 4;
                }
            }
            finally
            {
                if (bloqueoMiCache)
                    Monitor.Exit(objMiCache);
                if (bloqueoDirecCasa)
                    Monitor.Exit(objDirecCasa);
                if (bloqueoDirecVictima)
                    Monitor.Exit(objDirecVictima);
                if (bloqueoDirecBloque)
                    Monitor.Exit(objDirecBloque);
            }
        }

        public void storeConditional(int posMem, int regFuente)
        {
            /*Contexto contPrincipal = contextos.ElementAt(0);

            if (contPrincipal.registro[32] == posMem)
            {
                if (storeWord(posMem, regFuente))
                {
                    foreach (var p in procesadores)
                    {
                        foreach (var c in p.contextos)
                        {
                            if (p.id != this.id && c.loadLinkActivo)
                            {
                                var dir = getPosicion(c.registro[32]).Item1;
                                var miDir = getPosicion(contPrincipal.registro[32]).Item1;
                                if (dir == miDir)
                                {
                                    c.loadLinkActivo = false;
                                    c.registro[32] = -1;
                                }
                            }
                        }
                    }
                    contPrincipal.registro[regFuente] = 1;
                }
                else
                {
                    contPrincipal.registro[regFuente] = 0;
                }

                contPrincipal.loadLinkActivo = false;
            }*/
        }

        private int getNumeroProcesador(int bloque) 
        {
            return bloque / 8;
        }

        private Tuple<int, int> getPosicion(int direccion)
        {
            int bloque = (int)direccion / 16;
            int posicion = (direccion % 16) / 4;
            return new Tuple<int, int>(bloque, posicion);
        }

        public bool loadWord(int regFuente2, int posMem)
        {
            // seamos optimistas
            bool resultado = true;

            // banderas de locks
            bool bloqueoMiCache = false;
            bool bloqueoDirecVictima = false;
            bool bloqueoDirecBloque = false;

            // en caso que no se logre lock de direc bloque victima no deja pasar
            bool puedeContinuarDesdeBloqueVictima = true;

            // punteros a los objetos que hay que liberar
            object objMiCache = null;
            object objDirecVictima = null;
            object objDirecBloque = null;

            var direccion = getPosicion(posMem);
            Contexto contPrincipal = contextos.ElementAt(0);

            try
            {
                Monitor.TryEnter(this.cacheDatos, ref bloqueoMiCache);
                if(bloqueoMiCache)
                {
                    #region bloqueoMiCache
                    objMiCache = this.cacheDatos;
                    if (bloqueEnMiCache(direccion))
                    {
                        //HIT
                        contPrincipal.registro[regFuente2] = this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][direccion.Item2]; //????
                    }
                    else
                    {
                        // MISS
                        //revisa el estado de bloque víctima 
                        int estadoBloqueVictima = this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][CACHDAT_COL_ESTADO];
                        if (estadoBloqueVictima == ESTADO_COMPARTIDO || estadoBloqueVictima == ESTADO_MODIFICADO)
                        {
                            // pide directorio de bloque victima 
                            // lo bloquea 
                            int numeroBloqueVictima = this.blockMapDatos[direccion.Item1 % CACHDAT_FILAS];
                            int procesadorBloqueVictima = getNumeroProcesador(numeroBloqueVictima);
                            Monitor.TryEnter(procesadores.ElementAt(procesadorBloqueVictima).directorio, ref bloqueoDirecVictima);
                            if (bloqueoDirecVictima)
                            {
                                #region bloqueoDirecVictima
                                objDirecVictima = procesadores.ElementAt(procesadorBloqueVictima).directorio;
                                // bloqueo directorio victima
                                if (procesadorBloqueVictima == this.id)
                                {
                                    estoyEnRetraso = true;
                                    ciclosEnRetraso += 2; //ciclos que gasta en consulta directorio local
                                }
                                else
                                {
                                    estoyEnRetraso = true;
                                    ciclosEnRetraso += 4; //ciclos que gasta en consulta directorio remoto
                                }
                                if (estadoBloqueVictima == 1)
                                {
                                    // el bloque vicitma está C
                                    procesadores.ElementAt(procesadorBloqueVictima).directorio[direccion.Item1 % DIRECT_FILAS][this.id] = ESTADO_UNCACHED; // actualiza el directorio poniendo cero                                                                                                                 // poner I cache propia
                                    this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][CACHDAT_COL_ESTADO] = ESTADO_INVALIDO;
                                }
                                else if (estadoBloqueVictima == 2)
                                {
                                    // el bloque víctima está M
                                    // manda a guardar el bloque   
                                    // actualizar directorio
                                    // poner I cache propia
                                    for (int i = 0; i < 4; i++)
                                    {
                                        procesadores.ElementAt(procesadorBloqueVictima).memoriaPrincipal[numeroBloqueVictima % BLOQUES_COMP][i][0] = this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][i];
                                    }
                                    estoyEnRetraso = true;
                                    ciclosEnRetraso += 16;
                                    this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][CACHDAT_COL_ESTADO] = ESTADO_INVALIDO; //invalida la caché 
                                }
                                #endregion
                            }
                            else
                            {
                                contPrincipal.pc -= 4;
                                puedeContinuarDesdeBloqueVictima = false;
                                resultado = false;
                            }

                        }
                        if(puedeContinuarDesdeBloqueVictima)
                        {
                            int numProcBloque = getNumeroProcesador(direccion.Item1);
                            Monitor.TryEnter(procesadores.ElementAt(numProcBloque).directorio, ref bloqueoDirecBloque);
                            if (bloqueoDirecBloque)
                            {
                                #region bloqueoDirecBloque
                                objDirecBloque = procesadores.ElementAt(numProcBloque).directorio;
                                // tengo directorio bloque que quiero leer
                                if (numProcBloque == this.id)
                                {
                                    estoyEnRetraso = true;
                                    ciclosEnRetraso += 2; // ciclos que gasta en consulta directorio local
                                }
                                else
                                {
                                    estoyEnRetraso = true;
                                    ciclosEnRetraso += 4; // ciclos que gasta en consulta directorio remoto
                                }
                                int estadoBloque = procesadores.ElementAt(numProcBloque).directorio[direccion.Item1 % DIRECT_FILAS][DIRECT_COL_ESTADO];
                                // estados son U C M
                                switch (estadoBloque)
                                {

                                    /**
                                    case 1:
                                        // C
                                        // bloquea directorio 
                                        // actualiza 
                                        procesadores.ElementAt(numProcBloque).directorio[direccion.Item1 % 8][this.id] = 0; //lo pone como inválido
                                        //el id es el numero de procesador 
                                        break;
                                    **/
                                    case ESTADO_MODIFICADO:
                                        // M
                                        // bloquea directorio
                                        // 
                                        for (int i = 0; i < 4; i++)
                                        {
                                            procesadores.ElementAt(numProcBloque).memoriaPrincipal[direccion.Item1 % BLOQUES_COMP][i][0] = this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][i]; //??
                                        }
                                        // guarda en memoria, actualiza directorio
                                        // 
                                        procesadores.ElementAt(numProcBloque).directorio[direccion.Item1 % DIRECT_FILAS][this.id] = ESTADO_UNCACHED; //invalida
                                        break;
                                }

                                //jala el bloque de memoria
                                this.blockMapDatos[direccion.Item1 % CACHDAT_FILAS] = direccion.Item1;
                                this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][CACHDAT_COL_ESTADO] = ESTADO_COMPARTIDO; //pone en c en la cache 
                                for (int i = 0; i < 4; i++)
                                {
                                    this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][i] = procesadores.ElementAt(numProcBloque).memoriaPrincipal[direccion.Item1 % BLOQUES_COMP][i][0];
                                }
                                if (numProcBloque == this.id)
                                {
                                    estoyEnRetraso = true;
                                    ciclosEnRetraso += 16; //ciclos que gasta en cargar de memoria local
                                }
                                else
                                {
                                    estoyEnRetraso = true;
                                    ciclosEnRetraso += 32; //ciclos que gasta en cargar de memoria remoto
                                }
                                procesadores.ElementAt(numProcBloque).directorio[direccion.Item1 % DIRECT_FILAS][this.id] = ESTADO_COMPARTIDO; //pone en c en el directorio.
                                //pone en C en el directorio y en la cache
                                //libera

                                contPrincipal.registro[regFuente2] = this.cacheDatos[direccion.Item1 % CACHDAT_FILAS][direccion.Item2];
                                #endregion
                            }
                            else
                            {
                                contPrincipal.pc -= 4;
                                resultado = false;
                            }
                        }
                    }
                    #endregion
                }
                else
                {
                    // gg wp
                    contPrincipal.pc -= 4;
                    resultado = false;
                }
            }
            finally
            {
                if (bloqueoMiCache)
                    Monitor.Exit(objMiCache);
                if(bloqueoDirecVictima)
                    Monitor.Exit(objDirecVictima);
                if(bloqueoDirecBloque)
                    Monitor.Exit(objDirecBloque);
            }
            return resultado;
        }

        public void loadLink(int regFuente2, int posMem)
        {
            if(loadWord(regFuente2, posMem))
            {
                Contexto contPrincipal = contextos.ElementAt(0);
                contPrincipal.registro[32] = posMem; //Actualiza el valor de RL 
                contPrincipal.loadLinkActivo = true;
                //pone bandera de loadLinkActivo en true
            }
        }


        public void Iniciar()
        {
            while (contextos.Count > 0)
            {
                // Need to sync here
                sync.SignalAndWait();

                /// todo
                // console.WriteLine(String.Format("[Procesador #{0}] Hilillo #{1}, ciclo: {2}", id, contextos.ElementAt(0).id, cicloActual)); 

                if (!estoyEnRetraso)
                {

                    int pc = contextos.ElementAt(0).pc;
                    Tuple<int, int> posicion = getPosicion(pc);
                    if (blockMap[posicion.Item1 % 4] != posicion.Item1)
                    {
                        // Fallo de caché 
                        for (int j = 0; j < 4; j++)
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                //Console.WriteLine("i: "+i+" j: "+j);
                                cacheInstrucciones[posicion.Item1 % 4][j][i] = memoriaPrincipal[posicion.Item1][j][i];
                                //Console.WriteLine("[{0}] Cache[{1}],[{2}],[{3}]=[{4}]", id, posicion.Item1 % 4, j, i, cacheInstrucciones[posicion.Item1 % 4][j][i]);
                            }
                        }
                        blockMap[posicion.Item1 % 4] = posicion.Item1;

                        estoyEnRetraso = true;
                        ciclosEnRetraso = 16;
                        //Console.WriteLine("[{0}] Fallo de cache, ciclo: {1}", id, cicloActual);
                    }
                    else
                    {
                        /// @TODO Ejecutar mofo
                        console.WriteLine(String.Format("[{0}] ciclo: {1}, [{2}]: {3}", id, cicloActual, contextos.ElementAt(0).id, getStringInstruccion(cacheInstrucciones[posicion.Item1 % 4][posicion.Item2])));
                        bool res = manejoInstrucciones(cacheInstrucciones[posicion.Item1 % 4][posicion.Item2]);
                        if(res)
                        {
                            //Console.WriteLine("[{0}] Murio hilo {1}, ciclo: {2}", id, contextos.ElementAt(0).id, cicloActual);
                            contextos.ElementAt(0).cicloFinal = cicloActual;
                            contextosFinalizados.Add(contextos.ElementAt(0));
                            contextos.RemoveAt(0);// @TODO contmanejorolar out of bounds
                        }
                        else
                        {
                            quantum--;
                            if (quantum == 0)
                            {
                                // Hacer cambio de contexto!
                                //Console.WriteLine("[{0}] Cambio contexto, ciclo: {1}", id, cicloActual); 
                                ShiftLeft(contextos, 1);
                                if (contextos.ElementAt(0).cicloInicial == -1)
                                    contextos.ElementAt(0).cicloInicial = cicloActual;
                            }
                        }   
                    }
                }
                else
                {
                    // si hay fallo de cache, el quantum no avanza
                    if (ciclosEnRetraso == 0)
                    {
                        estoyEnRetraso = false;
                    }
                    else
                    {
                        ciclosEnRetraso--;
                    }
                }
                cicloActual++;
            }
            
            sync.RemoveParticipant();
        }        
    }

}
