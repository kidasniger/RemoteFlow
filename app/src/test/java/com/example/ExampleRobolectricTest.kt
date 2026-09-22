package com.example

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import com.example.domain.model.ConnectionState
import com.example.macros.DefaultMacros
import com.example.macros.MacroRepository
import com.example.network.DefaultRemotePcClient
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [36])
class ExampleRobolectricTest {

  @Test
  fun `read string from context`() {
    val context = ApplicationProvider.getApplicationContext<Context>()
    val appName = context.getString(R.string.app_name)
    assertEquals("RemoteFlow", appName)
  }

  @Test
  fun `verify macro repository default list`() {
    val client = DefaultRemotePcClient()
    val repo = MacroRepository(client)
    assertEquals(DefaultMacros.list.size, repo.macros.value.size)
  }
}

