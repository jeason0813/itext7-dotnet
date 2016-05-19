using System;
using NUnit.Framework;

namespace iTextSharp.IO.Image
{
	public class Jpeg2000Test
	{
		public const String sourceFolder = "../../resources/itextsharp/io/image/";

		/// <exception cref="System.IO.IOException"/>
		[Test]
		public virtual void OpenJpeg2000_1()
		{
			try
			{
				ImageData img = ImageDataFactory.Create(sourceFolder + "WP_20140410_001.JP2");
				Jpeg2000ImageHelper.ProcessImage(img);
			}
			catch (iTextSharp.IO.IOException e)
			{
				NUnit.Framework.Assert.AreEqual(iTextSharp.IO.IOException.UnsupportedBoxSizeEqEq0
					, e.Message);
			}
		}

		/// <exception cref="System.IO.IOException"/>
		[Test]
		public virtual void OpenJpeg2000_2()
		{
			ImageData img = ImageDataFactory.Create(sourceFolder + "WP_20140410_001.JPC");
			Jpeg2000ImageHelper.ProcessImage(img);
			NUnit.Framework.Assert.AreEqual(2592, img.GetWidth(), 0);
			NUnit.Framework.Assert.AreEqual(1456, img.GetHeight(), 0);
			NUnit.Framework.Assert.AreEqual(8, img.GetBpc());
		}
	}
}