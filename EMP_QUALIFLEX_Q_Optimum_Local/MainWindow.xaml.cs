// corrigé le 25/06/2020
// =====================================================================================
// EMP_QUALIFLEX_Q_Optimum_Local
// Morphologie mathématique multivaluée (images multibandes) avec ordonnancement vectoriel
// par la méthode QUALIFLEX (énumération des permutations, indice de concordance/discordance).
//
// Transformations calculées pour chaque taille i = 1..max de l'élément structurant (ES) :
//   érosion, dilatation, ouverture, fermeture, ouverture par reconstruction,
//   fermeture par reconstruction (+ export multibande ENVI .hdr).
//
// ES : disque. Le disque de taille i est obtenu par i itérations de l'ES de base B1
//      (disque de rayon 1 = 5 pixels), par associativité de la somme de Minkowski.
//      Chaque voisinage compte donc n = 5 pixels-vecteurs : n! = 120 permutations à évaluer.
// =====================================================================================
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows;
using SDColor = System.Drawing.Color;
using SDPoint = System.Drawing.Point;

namespace EMP_QUALIFLEX_Q_Optimum_Local
{
    public partial class MainWindow : Window
    {
        // Tolérance numérique pour comparer deux indices de concordance (nombres réels)
        private const double EPS = 1e-12;

        // Bandes chargées par l'utilisateur (une image panchromatique = une bande)
        List<Bitmap> imagesBmp = new List<Bitmap>();

        public MainWindow()
        {
            InitializeComponent();
        }

        // Dossier de sortie « IMAGES-résultat » : toujours juste sous le dossier du projet.
        // On remonte depuis le dossier de l'exécutable (bin\Debug\... ou Executable\) jusqu'au
        // dossier qui contient la solution (.sln) ; s'il n'y en a pas (exécutable copié seul sur
        // une autre machine), le dossier est créé à côté de l'exécutable.
        private static string DossierResultats()
        {
            string dossierExe = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                DirectoryInfo d = new DirectoryInfo(dossierExe);
                while (d != null)
                {
                    if (d.GetFiles("*.sln").Length > 0)
                        return Path.Combine(d.FullName, "IMAGES-résultat");
                    d = d.Parent;
                }
            }
            catch (Exception)
            {
                // Dossier parent illisible : on garde le dossier de l'exécutable
            }
            return Path.Combine(dossierExe, "IMAGES-résultat");
        }

        // ============================== CHARGEMENT DES BANDES ==============================
        private void button_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.Multiselect = true;
            openFile.DefaultExt = "png";
            openFile.Filter = "PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|BMP (*.bmp)|*.bmp|TIFF (*.tiff;*.tif)|*.tiff;*.tif";
            bool? ok = openFile.ShowDialog();
            if (ok != true || openFile.FileNames.Length == 0)
                return;
            // On libère les anciennes bandes avant d'en charger de nouvelles
            foreach (Bitmap old in imagesBmp)
                old.Dispose();
            imagesBmp.Clear();
            foreach (string filename in openFile.FileNames)
                imagesBmp.Add(new Bitmap(filename));
            MessageBox.Show(imagesBmp.Count + " bande(s) chargée(s).", "Chargement");
        }

        // Lecture sécurisée d'un entier
        private static bool TryParseInt(string s, out int v)
        {
            return int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                || int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out v);
        }

        // Lecture sécurisée d'un réel (accepte la virgule ou le point décimal)
        private static bool TryParseDouble(string s, out double v)
        {
            string t = (s ?? "").Trim().Replace(',', '.');
            return double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v);
        }

        // ============================== PROGRAMME PRINCIPAL ==============================
        private void button1_Click(object sender, RoutedEventArgs e)
        {
            // ---- 1. Vérification des données saisies ----
            if (imagesBmp.Count == 0)
            {
                MessageBox.Show("Chargez d'abord au moins une bande (bouton Parcourir).", "Erreur");
                return;
            }
            int max, itStabilite;
            double prStabilitePct;
            if (!TryParseInt(textBox.Text, out max) || max < 1)
            {
                MessageBox.Show("Taille maximale de l'ES invalide (entier >= 1).", "Erreur");
                return;
            }
            if (!TryParseInt(textBox2.Text, out itStabilite) || itStabilite < 0)
            {
                MessageBox.Show("Nombre d'itérations de stabilité invalide (entier >= 0 ; 0 = illimité).", "Erreur");
                return;
            }
            if (!TryParseDouble(textBox3.Text, out prStabilitePct) || prStabilitePct < 0 || prStabilitePct > 100)
            {
                MessageBox.Show("Pourcentage de ressemblance invalide (0 à 100).", "Erreur");
                return;
            }
            double prStabilite = prStabilitePct / 100.0;

            // Poids des bandes (séparés par des points-virgules)
            string[] parts = (textBox4.Text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            double[] poids = new double[parts.Length];
            for (int p = 0; p < parts.Length; p++)
            {
                if (!TryParseDouble(parts[p], out poids[p]) || poids[p] < 0)
                {
                    MessageBox.Show("Poids invalides (nombres positifs ou nuls). Exemple : 0,45;0,35;0,20", "Erreur");
                    return;
                }
            }
            if (poids.Length != imagesBmp.Count)
            {
                MessageBox.Show("Il faut autant de poids que de bandes (" + imagesBmp.Count + " bande(s), " + poids.Length + " poids).", "Erreur");
                return;
            }
            // QUALIFLEX utilise des poids w_k avec sum(w_k) = 1 : si la somme vaut 100 (ou autre), on normalise
            double sumW = 0;
            for (int p = 0; p < poids.Length; p++) sumW += poids[p];
            if (sumW <= 0)
            {
                MessageBox.Show("La somme des poids doit être strictement positive.", "Erreur");
                return;
            }
            if (Math.Abs(sumW - 1.0) > 1e-9)
                for (int p = 0; p < poids.Length; p++) poids[p] /= sumW;

            // Toutes les bandes doivent avoir la même taille
            int w0 = imagesBmp[0].Width, h0 = imagesBmp[0].Height;
            for (int k = 1; k < imagesBmp.Count; k++)
            {
                if (imagesBmp[k].Width != w0 || imagesBmp[k].Height != h0)
                {
                    MessageBox.Show("Toutes les bandes doivent avoir la même taille.", "Erreur");
                    return;
                }
            }

            // ---- 2. Dossier de sortie ----
            string outDir = DossierResultats();
            Directory.CreateDirectory(outDir);

            // ---- 3. Conversion des bitmaps en matrices d'entiers [x, y] (une matrice par bande) ----
            List<int[,]> imagesMat;
            try
            {
                imagesMat = bmpToMat(imagesBmp);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible de lire les images : " + ex.Message, "Erreur");
                return;
            }

            try
            {
                // Listes de toutes les reconstructions (toutes tailles d'ES, toutes bandes) pour l'export ENVI
                List<int[,]> enviOuvert = new List<int[,]>();
                List<int[,]> enviFerme = new List<int[,]>();
                List<string> nomsOuvert = new List<string>();
                List<string> nomsFerme = new List<string>();

                // ---- 4. Boucle sur la taille i de l'ES (disque) ----
                for (int i = 1; i <= max; i++)
                {
                    List<int[,]> imagesErodeInit = new List<int[,]>();
                    List<int[,]> imagesDilateInit = new List<int[,]>();
                    List<int[,]> imagesOuvertesStandards = new List<int[,]>();
                    List<int[,]> imagesFermeesStandards = new List<int[,]>();

                    // Érosion et dilatation multivaluées de taille i
                    ErosionDilatationInit(imagesMat, poids, ref imagesErodeInit, ref imagesDilateInit, i);
                    // Ouverture et fermeture standard de taille i
                    OuvertureFermetureStandard(imagesErodeInit, imagesDilateInit, poids, ref imagesOuvertesStandards, ref imagesFermeesStandards, i);

                    // Sauvegarde des résultats (une image TIFF par bande)
                    for (int k = 0; k < imagesBmp.Count; k++)
                    {
                        Bitmap sortieErod = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                        Bitmap sortieDilat = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                        Bitmap sortieOuverteStandard = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                        Bitmap sortieFermeeStandard = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                        for (int x = 0; x < imagesBmp[k].Width; x++)
                            for (int y = 0; y < imagesBmp[k].Height; y++)
                            {
                                sortieErod.SetPixel(x, y, Gray(imagesErodeInit[k][x, y]));
                                sortieDilat.SetPixel(x, y, Gray(imagesDilateInit[k][x, y]));
                                sortieOuverteStandard.SetPixel(x, y, Gray(imagesOuvertesStandards[k][x, y]));
                                sortieFermeeStandard.SetPixel(x, y, Gray(imagesFermeesStandards[k][x, y]));
                            }
                        sortieErod.Save(Path.Combine(outDir, "Erod B_" + k + " ES_" + i + ".tiff"));
                        sortieDilat.Save(Path.Combine(outDir, "Dilat B_" + k + " ES_" + i + ".tiff"));
                        sortieOuverteStandard.Save(Path.Combine(outDir, "OuvertureStandard B_" + k + " ES_" + i + ".tiff"));
                        sortieFermeeStandard.Save(Path.Combine(outDir, "FermetureStandard B_" + k + " ES_" + i + ".tiff"));
                        sortieErod.Dispose();
                        sortieDilat.Dispose();
                        sortieOuverteStandard.Dispose();
                        sortieFermeeStandard.Dispose();
                    }

                    // Ouverture et fermeture par reconstruction (arrêt par stabilité)
                    List<int[,]> gNewFerme = new List<int[,]>();
                    List<int[,]> gNewOuvert = new List<int[,]>();
                    Reconstruction(imagesMat, imagesErodeInit, imagesDilateInit, poids, itStabilite, prStabilite, ref gNewOuvert, ref gNewFerme);

                    for (int k = 0; k < imagesBmp.Count; k++)
                    {
                        Bitmap sortieFerme = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                        Bitmap sortieOuvert = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                        for (int x = 0; x < imagesBmp[k].Width; x++)
                            for (int y = 0; y < imagesBmp[k].Height; y++)
                            {
                                sortieFerme.SetPixel(x, y, Gray(gNewFerme[k][x, y]));
                                sortieOuvert.SetPixel(x, y, Gray(gNewOuvert[k][x, y]));
                            }
                        sortieFerme.Save(Path.Combine(outDir, "FermeReconstruction B_" + k + " ES_" + i + ".tiff"));
                        sortieOuvert.Save(Path.Combine(outDir, "OuvertReconstruction B_" + k + " ES_" + i + ".tiff"));
                        sortieFerme.Dispose();
                        sortieOuvert.Dispose();
                        // Mémorisation pour les fichiers multibandes ENVI
                        enviOuvert.Add((int[,])gNewOuvert[k].Clone());
                        enviFerme.Add((int[,])gNewFerme[k].Clone());
                        nomsOuvert.Add("OuvertReconstruction B_" + k + " ES_" + i);
                        nomsFerme.Add("FermeReconstruction B_" + k + " ES_" + i);
                    }
                }

                // ---- 5. Fichiers multibandes ENVI (.img + .hdr) ----
                List<int[,]> enviTout = new List<int[,]>();
                List<string> nomsTout = new List<string>();
                enviTout.AddRange(enviOuvert);
                enviTout.AddRange(enviFerme);
                nomsTout.AddRange(nomsOuvert);
                nomsTout.AddRange(nomsFerme);
                WriteEnviMultiband(outDir, "EXTENDED-PROFIL-MOR-multibande", w0, h0, enviTout, nomsTout);
                WriteEnviMultiband(outDir, "OuvertReconstruction-multibande", w0, h0, enviOuvert, nomsOuvert);
                WriteEnviMultiband(outDir, "FermeReconstruction-multibande", w0, h0, enviFerme, nomsFerme);

                MessageBox.Show("Traitement terminé.\nRésultats (TIFF + ENVI .hdr) dans :\n" + outDir, "EMP_QUALIFLEX_Q_Optimum_Local");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur pendant le calcul :\n" + ex.Message, "Erreur");
            }
        }

        // Convertit une valeur entière en niveau de gris (borné à [0, 255])
        private static SDColor Gray(int v)
        {
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return SDColor.FromArgb(v, v, v);
        }

        // Écrit un fichier multibande ENVI (.img en BSQ, 8 bits + .hdr)
        private static void WriteEnviMultiband(string outDir, string baseName, int width, int height, List<int[,]> bands, List<string> bandNames)
        {
            if (bands == null || bands.Count == 0)
                return;
            string imgPath = Path.Combine(outDir, baseName + ".img");
            string hdrPath = Path.Combine(outDir, baseName + ".hdr");
            using (FileStream fs = new FileStream(imgPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buf = new byte[width * height];
                for (int b = 0; b < bands.Count; b++)
                {
                    int idx = 0;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int v = bands[b][x, y];
                            if (v < 0) v = 0;
                            if (v > 255) v = 255;
                            buf[idx++] = (byte)v;
                        }
                    }
                    fs.Write(buf, 0, buf.Length);
                }
            }
            var names = new System.Text.StringBuilder();
            for (int i = 0; i < bandNames.Count; i++)
            {
                if (i > 0) names.Append(",\r\n ");
                names.Append(bandNames[i]);
            }
            string hdr =
                "ENVI\r\n" +
                "description = { EMP_QUALIFLEX_Q_Optimum_Local, " + baseName + " }\r\n" +
                "samples = " + width + "\r\n" +
                "lines = " + height + "\r\n" +
                "bands = " + bands.Count + "\r\n" +
                "header offset = 0\r\n" +
                "file type = ENVI Standard\r\n" +
                "data type = 1\r\n" +
                "interleave = bsq\r\n" +
                "byte order = 0\r\n" +
                "band names = {\r\n " + names + "\r\n}\r\n";
            File.WriteAllText(hdrPath, hdr, System.Text.Encoding.ASCII);
        }

        // ============================== ÉLÉMENT STRUCTURANT DISQUE ==============================
        // Décalages (dx, dy) des pixels couverts par un disque de rayon r centré sur (0,0).
        // r=1 -> 5 pixels, r=2 -> 13 pixels, r=3 -> 29 pixels.
        // L'ordre de parcours (dx puis dy croissants) définit l'INDICE SPATIAL des pixels de l'ES.
        private List<SDPoint> GetDiskOffsets(int r)
        {
            var offs = new List<SDPoint>();
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    if (dx * dx + dy * dy <= r * r) offs.Add(new SDPoint(dx, dy));
            return offs;
        }

        // Vrai si le disque centré en (x,y) déborde de l'image : pixel de bord, non traité
        // (il conserve sa valeur précédente)
        private bool IsBorder(int x, int y, int W, int H, List<SDPoint> offs)
        {
            foreach (var p in offs)
            {
                int nx = x + p.X, ny = y + p.Y;
                if (nx < 0 || ny < 0 || nx >= W || ny >= H) return true;
            }
            return false;
        }

        // ============================== RECONSTRUCTION MORPHOLOGIQUE ==============================
        // Ouverture par reconstruction : R^delta_f( epsilon_Bi(f) )  (marqueur = érodé, masque = f)
        // Fermeture par reconstruction : R^epsilon_f( delta_Bi(f) )  (marqueur = dilaté, masque = f)
        // On itère les dilatations/érosions géodésiques d'ordre 1 jusqu'à stabilité, ou jusqu'à
        // atteindre le nombre d'itérations maximal fixé (0 = illimité), ou jusqu'au pourcentage de
        // ressemblance demandé, ou jusqu'à la détection d'un cycle (état déjà rencontré).
        private void Reconstruction(List<int[,]> imagesMat, List<int[,]> imagesErodeInit, List<int[,]> imagesDilateInit, double[] poids, int itStabilite, double prStabilite, ref List<int[,]> gNewOuvert, ref List<int[,]> gNewFerme)
        {
            List<int[,]> gLastFerme = new List<int[,]>();
            List<int[,]> gLastOuvert = new List<int[,]>();
            for (int k = 0; k < imagesMat.Count; k++)
            {
                gNewFerme.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                gNewOuvert.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                // Marqueurs initiaux : image érodée (ouverture) et image dilatée (fermeture)
                gLastOuvert.Add((int[,])imagesErodeInit[k].Clone());
                gLastFerme.Add((int[,])imagesDilateInit[k].Clone());
            }
            int toleranceStabilite = 0;
            if (itStabilite == 0) itStabilite = int.MaxValue; // 0 = nombre d'itérations non limité
            bool stopErod = false, stopDilat = false;

            // Détection de cycle : la comparaison QUALIFLEX de deux vecteurs (vote majoritaire pondéré)
            // n'est pas transitive (on peut avoir A > B > C > A). La suite des images géodésiques peut donc
            // devenir périodique (A -> B -> ... -> A) sans jamais se stabiliser. On mémorise l'empreinte de
            // chaque état rencontré ; si un état réapparaît, la stabilité ne sera jamais atteinte et on arrête
            // la transformation concernée (évite une boucle infinie avec 0 itération = illimité et 100 %).
            HashSet<ulong> etatsOuvert = new HashSet<ulong>();
            HashSet<ulong> etatsFerme = new HashSet<ulong>();
            etatsOuvert.Add(Empreinte(gLastOuvert));
            etatsFerme.Add(Empreinte(gLastFerme));
            bool cycleOuvert = false, cycleFerme = false;

            while (((!stopDilat) || (!stopErod)) && (toleranceStabilite < itStabilite))
            {
                stopErod = true; stopDilat = true;
                // Une étape de dilatation / érosion géodésique d'ordre 1
                ErosionDilatationGeodesique(imagesMat, gLastFerme, gLastOuvert, poids, prStabilite, ref gNewFerme, ref gNewOuvert, ref stopErod, ref stopDilat);
                // Les images calculées deviennent les marqueurs de l'itération suivante
                for (int k = 0; k < imagesMat.Count; k++)
                {
                    gLastOuvert[k] = (int[,])gNewOuvert[k].Clone();
                    gLastFerme[k] = (int[,])gNewFerme[k].Clone();
                }
                toleranceStabilite++;

                // Un état déjà rencontré => suite périodique => arrêt de la transformation concernée
                if (!etatsOuvert.Add(Empreinte(gLastOuvert))) cycleOuvert = true;
                if (!etatsFerme.Add(Empreinte(gLastFerme))) cycleFerme = true;
                if (cycleOuvert) stopDilat = true;
                if (cycleFerme) stopErod = true;
            }
        }

        // Empreinte 64 bits (FNV-1a) d'une image multibande, utilisée pour reconnaître un état déjà vu
        private static ulong Empreinte(List<int[,]> img)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                foreach (int[,] bande in img)
                    foreach (int v in bande)
                    {
                        h ^= (uint)v;
                        h *= 1099511628211UL;
                    }
                return h;
            }
        }

        // Une étape géodésique d'ordre 1 avec B1 (disque de rayon 1) :
        //   dilatation géodésique : delta_f^(1)(h)   = infimum ( delta_B1(h),   f )  -> ouverture
        //   érosion géodésique    : epsilon_f^(1)(h) = supremum( epsilon_B1(h), f )  -> fermeture
        // Le supremum/infimum de deux vecteurs est déterminé par l'ordre QUALIFLEX.
        private void ErosionDilatationGeodesique(List<int[,]> imagesMat, List<int[,]> gLastFerme, List<int[,]> gLastOuvert, double[] poids, double prStabilite, ref List<int[,]> gNewFerme, ref List<int[,]> gNewOuvert, ref bool stopErod, ref bool stopDilat)
        {
            int W = imagesMat[0].GetLength(0), H = imagesMat[0].GetLength(1);
            var offsB1 = GetDiskOffsets(1);
            int ressemblanceOuverture = 0, ressemblanceFermeture = 0;
            for (int x = 0; x < W; x++)
            {
                for (int y = 0; y < H; y++)
                {
                    if (IsBorder(x, y, W, H, offsB1))
                    {
                        // Pixel de bord : valeur précédente conservée
                        for (int k = 0; k < imagesMat.Count; k++)
                        {
                            gNewFerme[k][x, y] = gLastFerme[k][x, y];
                            gNewOuvert[k][x, y] = gLastOuvert[k][x, y];
                        }
                    }
                    else
                    {
                        // Infimum (érosion) du marqueur de fermeture et supremum (dilatation) du marqueur d'ouverture dans B1
                        int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                        MinMaxVecteurs(gLastFerme, gLastOuvert, poids, x, y, ref sMin, ref tMin, ref sMax, ref tMax, 1);

                        // Érosion géodésique : supremum( epsilon_B1(h), f )
                        int cmpFerme = CompareDeuxVecteursQUALIFLEX(imagesMat, x, y, gLastFerme, sMin, tMin, poids);
                        if (cmpFerme < 0) // f < epsilon_B1(h) => le supremum est epsilon_B1(h)
                            for (int k = 0; k < imagesMat.Count; k++) gNewFerme[k][x, y] = gLastFerme[k][sMin, tMin];
                        else              // f >= epsilon_B1(h) => le supremum est f
                            for (int k = 0; k < imagesMat.Count; k++) gNewFerme[k][x, y] = imagesMat[k][x, y];

                        // Dilatation géodésique : infimum( delta_B1(h), f )
                        int cmpOuvert = CompareDeuxVecteursQUALIFLEX(gLastOuvert, sMax, tMax, imagesMat, x, y, poids);
                        if (cmpOuvert < 0) // delta_B1(h) < f => l'infimum est delta_B1(h)
                            for (int k = 0; k < imagesMat.Count; k++) gNewOuvert[k][x, y] = gLastOuvert[k][sMax, tMax];
                        else               // delta_B1(h) >= f => l'infimum est f
                            for (int k = 0; k < imagesMat.Count; k++) gNewOuvert[k][x, y] = imagesMat[k][x, y];

                        // Test de stabilité : le pixel est-il identique à l'itération précédente (toutes bandes) ?
                        int locOuv = 0, locFerm = 0;
                        for (int k = 0; k < imagesMat.Count; k++)
                        {
                            if (gNewFerme[k][x, y] != gLastFerme[k][x, y]) stopErod = false; else locFerm++;
                            if (gNewOuvert[k][x, y] != gLastOuvert[k][x, y]) stopDilat = false; else locOuv++;
                        }
                        if (locOuv == imagesMat.Count) ressemblanceOuverture++;
                        if (locFerm == imagesMat.Count) ressemblanceFermeture++;
                    }
                }
            }
            // Stabilité par pourcentage de ressemblance entre deux images successives
            double tauxOuv = (double)ressemblanceOuverture / (W * H);
            double tauxFerm = (double)ressemblanceFermeture / (W * H);
            if (tauxOuv >= prStabilite) stopDilat = true;
            if (tauxFerm >= prStabilite) stopErod = true;
        }

        // Compare deux pixels-vecteurs A(xa,ya) de imgA et B(xb,yb) de imgB au sens de l'ordre QUALIFLEX
        // appliqué à l'ensemble {A, B} (n = 2, donc 2! = 2 permutations : (A,B) et (B,A)) :
        //   I(A,B) = somme_k w_k * s_k, où s_k = +1 si A > B sur la bande k, -1 si A < B, 0 si égalité
        //   (indice de concordance/discordance de la permutation (A,B)) ; I(B,A) = -I(A,B).
        //   La permutation optimale place A en tête si I(A,B) > I(B,A), B en tête sinon.
        // Ex aequo (I(A,B) = I(B,A)) : départage par l'indice spatial des pixels (le plus grand indice est en tête),
        // comme dans le calcul du voisinage (voir SearchPerm).
        // Retourne -1 si A < B, 0 si A = B (même position), 1 si A > B.
        private int CompareDeuxVecteursQUALIFLEX(List<int[,]> imgA, int xa, int ya, List<int[,]> imgB, int xb, int yb, double[] poids)
        {
            int m = imgA.Count;
            double I_AB = 0, I_BA = 0;
            for (int k = 0; k < m; k++)
            {
                int va = imgA[k][xa, ya];
                int vb = imgB[k][xb, yb];
                int signAB = va > vb ? 1 : (va < vb ? -1 : 0);
                I_AB += poids[k] * signAB;
                I_BA -= poids[k] * signAB;
            }
            if (Math.Abs(I_AB - I_BA) > EPS) return I_AB < I_BA ? -1 : 1;
            // Ex aequo : indice spatial (parcours x puis y)
            int H = imgA[0].GetLength(1);
            int idxA = xa * H + ya;
            int idxB = xb * H + yb;
            if (idxA < idxB) return -1;
            if (idxA > idxB) return 1;
            return 0;
        }

        // ============================== OUVERTURE / FERMETURE STANDARD ==============================
        // Ouverture = dilatation de l'érodé ; fermeture = érosion du dilaté (même ES de taille 'rayon'),
        // obtenues par 'rayon' itérations de l'ES de base B1.
        private void OuvertureFermetureStandard(List<int[,]> imagesErodeInit, List<int[,]> imagesDilateInit, double[] poids, ref List<int[,]> imagesOuvertesStandards, ref List<int[,]> imagesFermeesStandards, int rayon)
        {
            List<int[,]> imagesErodPrec = new List<int[,]>();
            List<int[,]> imagesDilatePrec = new List<int[,]>();
            for (int k = 0; k < imagesErodeInit.Count; k++)
            {
                imagesOuvertesStandards.Add(new int[imagesErodeInit[0].GetLength(0), imagesErodeInit[0].GetLength(1)]);
                imagesFermeesStandards.Add(new int[imagesErodeInit[0].GetLength(0), imagesErodeInit[0].GetLength(1)]);
                // Fermeture : on érode l'image dilatée ; ouverture : on dilate l'image érodée
                imagesErodPrec.Add((int[,])imagesDilateInit[k].Clone());
                imagesDilatePrec.Add((int[,])imagesErodeInit[k].Clone());
            }
            var offsB1 = GetDiskOffsets(1);
            int W = imagesErodeInit[0].GetLength(0), H = imagesErodeInit[0].GetLength(1);
            for (int elemStruct = 0; elemStruct < rayon; elemStruct++)
            {
                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                    {
                        if (IsBorder(x, y, W, H, offsB1))
                        {
                            // Pixel de bord : valeur précédente conservée
                            for (int k = 0; k < imagesErodeInit.Count; k++)
                            {
                                imagesOuvertesStandards[k][x, y] = imagesDilatePrec[k][x, y];
                                imagesFermeesStandards[k][x, y] = imagesErodPrec[k][x, y];
                            }
                        }
                        else
                        {
                            int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                            MinMaxVecteurs(imagesErodPrec, imagesDilatePrec, poids, x, y, ref sMin, ref tMin, ref sMax, ref tMax, 1);
                            for (int k = 0; k < imagesErodeInit.Count; k++)
                            {
                                imagesFermeesStandards[k][x, y] = imagesErodPrec[k][sMin, tMin];
                                imagesOuvertesStandards[k][x, y] = imagesDilatePrec[k][sMax, tMax];
                            }
                        }
                    }
                for (int k = 0; k < imagesErodeInit.Count; k++)
                {
                    imagesErodPrec[k] = (int[,])imagesFermeesStandards[k].Clone();
                    imagesDilatePrec[k] = (int[,])imagesOuvertesStandards[k].Clone();
                }
            }
        }

        // ============================== ÉROSION / DILATATION ==============================
        // Érosion (infimum) et dilatation (supremum) multivaluées de taille 'rayon', obtenues par
        // 'rayon' itérations de l'ES de base B1 (ε_Bλ = ε_B1^λ, δ_Bλ = δ_B1^λ).
        private void ErosionDilatationInit(List<int[,]> imagesMat, double[] poids, ref List<int[,]> imagesErodeInit, ref List<int[,]> imagesDilateInit, int rayon)
        {
            List<int[,]> imagesErodPrec = new List<int[,]>();
            List<int[,]> imagesDilatePrec = new List<int[,]>();
            for (int k = 0; k < imagesMat.Count; k++)
            {
                imagesDilateInit.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                imagesErodeInit.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                imagesErodPrec.Add((int[,])imagesMat[k].Clone());
                imagesDilatePrec.Add((int[,])imagesMat[k].Clone());
            }
            var offsB1 = GetDiskOffsets(1);
            int W = imagesMat[0].GetLength(0), H = imagesMat[0].GetLength(1);
            for (int elemStruct = 0; elemStruct < rayon; elemStruct++)
            {
                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                    {
                        if (IsBorder(x, y, W, H, offsB1))
                        {
                            // Pixel de bord : valeur précédente conservée
                            for (int k = 0; k < imagesMat.Count; k++)
                            {
                                imagesDilateInit[k][x, y] = imagesDilatePrec[k][x, y];
                                imagesErodeInit[k][x, y] = imagesErodPrec[k][x, y];
                            }
                        }
                        else
                        {
                            int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                            MinMaxVecteurs(imagesErodPrec, imagesDilatePrec, poids, x, y, ref sMin, ref tMin, ref sMax, ref tMax, 1);
                            for (int k = 0; k < imagesMat.Count; k++)
                            {
                                imagesErodeInit[k][x, y] = imagesErodPrec[k][sMin, tMin];
                                imagesDilateInit[k][x, y] = imagesDilatePrec[k][sMax, tMax];
                            }
                        }
                    }
                for (int k = 0; k < imagesMat.Count; k++)
                {
                    imagesErodPrec[k] = (int[,])imagesErodeInit[k].Clone();
                    imagesDilatePrec[k] = (int[,])imagesDilateInit[k].Clone();
                }
            }
        }

        // ============================== QUALIFLEX ==============================
        // Ordonne les n pixels-vecteurs du voisinage (disque de rayon 'rayonDisque' centré en (x,y)) :
        //   1) matrice de décision n x m (lignes = pixels-vecteurs, colonnes = bandes) puis matrice de rangs E
        //      (rang 1 = plus grande valeur de la bande ; ex aequo = même rang)
        //   2) énumération des n! permutations R_l ; pour chaque bande k, indice I_kl = somme, sur les paires
        //      (a_p, a_q) avec a_p avant a_q dans R_l, de +1 (concordance), 0 (ex aequo), -1 (discordance)
        //   3) indice global I_l = somme_k w_k * I_kl ; permutation optimale R* = argmax I_l
        //   4) supremum = tête de R* (dilatation, calculé sur imagesDilatePrec)
        //      infimum  = queue de R* (érosion, calculé sur imagesErodPrec)
        // Sorties : coordonnées (sMin,tMin) de l'infimum et (sMax,tMax) du supremum.
        private void MinMaxVecteurs(List<int[,]> imagesErodPrec, List<int[,]> imagesDilatePrec, double[] poids, int x, int y, ref int sMin, ref int tMin, ref int sMax, ref int tMax, int rayonDisque)
        {
            var offs = GetDiskOffsets(rayonDisque);
            int n = offs.Count;              // 5 pour B1
            int m = imagesErodPrec.Count;    // nombre de bandes

            // L'énumération exhaustive de n! permutations n'est possible que pour n petit (n = 5 avec B1 : 120 permutations)
            if (n > 9)
                throw new InvalidOperationException("QUALIFLEX : énumération de n! permutations impossible pour n = " + n + " (l'ES de base B1 donne n = 5).");

            // Matrices de décision (valeurs brutes) n x m pour l'érosion et pour la dilatation
            int[,] matErod = new int[n, m];
            int[,] matDilat = new int[n, m];
            for (int i = 0; i < n; i++)
            {
                int xi = x + offs[i].X, yi = y + offs[i].Y;
                for (int k = 0; k < m; k++)
                {
                    matErod[i, k] = imagesErodPrec[k][xi, yi];
                    matDilat[i, k] = imagesDilatePrec[k][xi, yi];
                }
            }

            // Permutation optimale pour chaque image
            int[] bestPermErod = FindBestPermutation(matErod, poids, n, m);
            int[] bestPermDilat = FindBestPermutation(matDilat, poids, n, m);

            // Tête de R* = supremum (dilatation) ; queue de R* = infimum (érosion)
            sMax = x + offs[bestPermDilat[0]].X; tMax = y + offs[bestPermDilat[0]].Y;
            sMin = x + offs[bestPermErod[n - 1]].X; tMin = y + offs[bestPermErod[n - 1]].Y;
        }

        // Matrice de rangs E (n x m) : E[i,k] = 1 + nombre de pixels-vecteurs strictement meilleurs
        // (valeur strictement plus grande) que a_i sur la bande k ; les ex aequo ont le même rang.
        private int[,] MatriceRangs(int[,] mat, int n, int m)
        {
            int[,] E = new int[n, m];
            for (int k = 0; k < m; k++)
                for (int i = 0; i < n; i++)
                {
                    int rang = 1;
                    for (int j = 0; j < n; j++)
                        if (mat[j, k] > mat[i, k]) rang++;
                    E[i, k] = rang;
                }
            return E;
        }

        // Recherche de la permutation optimale R* (celle qui maximise l'indice global I_l)
        private int[] FindBestPermutation(int[,] mat, double[] poids, int n, int m)
        {
            int[,] E = MatriceRangs(mat, n, m);
            int[] perm = new int[n];
            bool[] used = new bool[n];
            int[] bestPerm = new int[n];
            double bestScore = double.NegativeInfinity;
            SearchPerm(0, perm, used, E, poids, n, m, ref bestScore, bestPerm);
            return bestPerm;
        }

        // Énumération récursive des permutations. Les indices spatiaux sont essayés par ordre DÉCROISSANT et une
        // permutation ne remplace la meilleure que si son indice I_l est strictement supérieur : en cas d'ex aequo
        // entre permutations, R* est la première rencontrée, c'est-à-dire celle qui place en tête le plus grand
        // indice spatial (R* est ainsi unique, et cohérent avec le départage utilisé pour PROMETHEE).
        private void SearchPerm(int depth, int[] perm, bool[] used, int[,] E, double[] poids, int n, int m, ref double bestScore, int[] bestPerm)
        {
            if (depth == n)
            {
                // Indice global I_l = somme_k w_k * I_kl
                double Il = 0;
                for (int k = 0; k < m; k++)
                {
                    int Ikl = 0;
                    for (int p = 0; p < n; p++)
                        for (int q = p + 1; q < n; q++)
                        {
                            int ap = perm[p], aq = perm[q];
                            // a_p précède a_q dans la permutation : concordance (+1) si a_p est mieux classé
                            // (rang plus petit) que a_q sur la bande k, discordance (-1) dans le cas contraire, 0 si ex aequo
                            int I = E[ap, k] < E[aq, k] ? 1 : (E[ap, k] > E[aq, k] ? -1 : 0);
                            Ikl += I;
                        }
                    Il += poids[k] * Ikl;
                }
                if (Il > bestScore + EPS)
                {
                    bestScore = Il;
                    Array.Copy(perm, bestPerm, n);
                }
                return;
            }
            for (int i = n - 1; i >= 0; i--)
            {
                if (used[i]) continue;
                used[i] = true;
                perm[depth] = i;
                SearchPerm(depth + 1, perm, used, E, poids, n, m, ref bestScore, bestPerm);
                used[i] = false;
            }
        }

        // ============================== CONVERSION BITMAP -> MATRICES ==============================
        // Une matrice int[x, y] par bande (valeur du canal rouge = niveau de gris)
        private List<int[,]> bmpToMat(List<Bitmap> imagesBmp)
        {
            List<int[,]> imagesMat = new List<int[,]>();
            for (int z = 0; z < imagesBmp.Count; z++)
            {
                imagesMat.Add(new int[imagesBmp[z].Width, imagesBmp[z].Height]);
                for (int x = 0; x < imagesBmp[z].Width; x++)
                    for (int y = 0; y < imagesBmp[z].Height; y++)
                        imagesMat[z][x, y] = imagesBmp[z].GetPixel(x, y).R;
            }
            return imagesMat;
        }
    }
}
